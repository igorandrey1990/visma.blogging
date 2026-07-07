using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Visma.Blogging.Api;
using Visma.Blogging.Api.Health;
using Visma.Blogging.Api.Swagger;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Infrastructure;
using Visma.Blogging.Infrastructure.Health;
using Visma.Blogging.Infrastructure.Logging;
using Visma.Blogging.Infrastructure.Messaging;
using Visma.Blogging.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// Controllers are the inbound HTTP adapter. JSON and XML formatters stay here,
// so the application layer remains independent from the wire format.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .AddXmlSerializerFormatters();

builder.Services.AddEndpointsApiExplorer();
// Versioning makes /api/v1/post the canonical public contract while still
// allowing older routes to exist separately for backward compatibility.
builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen(options =>
{
    // The operation filter documents headers that are not part of the JSON/XML body.
    options.OperationFilter<IdempotencyHeaderOperationFilter>();
    options.SupportNonNullableReferenceTypes();

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var mongoOptions = builder.Configuration.GetSection(MongoBlogStoreOptions.SectionName).Get<MongoBlogStoreOptions>()
    ?? new MongoBlogStoreOptions();
var rabbitMqOptions = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();
var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];

// Infrastructure adapters are registered behind application ports. Handlers
// depend on interfaces, while Program.cs decides the concrete Mongo/RabbitMQ implementations.
builder.Services.AddSingleton(mongoOptions);
builder.Services.AddSingleton(rabbitMqOptions);
builder.Services.AddSingleton<MongoDB.Driver.IMongoClient>(_ => new MongoDB.Driver.MongoClient(mongoOptions.ConnectionString));
builder.Services.AddSingleton<MongoRetryPolicy>();
builder.Services.AddSingleton<MongoBlogStore>();
builder.Services.AddSingleton<ICreatePostIdempotencyStore, MongoCreatePostIdempotencyStore>();
builder.Services.AddSingleton<MongoOutboxStore>();
builder.Services.AddSingleton<IOutboxReader>(provider => provider.GetRequiredService<MongoOutboxStore>());
builder.Services.AddSingleton<IRabbitMqMessagePublisher, RabbitMqMessagePublisher>();
builder.Services.AddSingleton<MongoLogQueue>();
builder.Services.AddSingleton<ILoggerProvider, MongoLoggerProvider>();
builder.Services.AddHostedService<MongoLogWriterBackgroundService>();
builder.Services.AddHostedService<OutboxPublisherBackgroundService>();
// Startup creates required MongoDB indexes and TTL cleanup rules before traffic is handled.
builder.Services.AddHostedService<MongoIndexInitializer>();
builder.Services.AddSingleton<IPostCreationStore>(provider => provider.GetRequiredService<MongoBlogStore>());
builder.Services.AddSingleton<IPostQueryStore>(provider => provider.GetRequiredService<MongoBlogStore>());
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton<IValidator<CreatePostCommand>, CreatePostCommandValidator>();
builder.Services.AddSingleton<IValidator<GetPostByIdQuery>, GetPostByIdQueryValidator>();
builder.Services.AddScoped<ICommandHandler<CreatePostCommand, PostResponse>, CreatePostCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetPostByIdQuery, PostResponse>, GetPostByIdQueryHandler>();
builder.Services.AddScoped<CreatePostEndpointService>();

// Readiness checks validate external dependencies. Liveness is mapped below as a lightweight process check.
builder.Services.AddHealthChecks()
    .AddCheck<MongoDbHealthCheck>("mongodb", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

// Separate policies reflect different risk levels: writes are expensive and mutate state,
// reads are cheaper and can tolerate a higher limit.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("post-write", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 20,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        }));
    options.AddPolicy("post-read", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 120,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        }));
});

// OpenTelemetry provides trace/metric data for production observability. OTLP is optional
// and configured externally; console export is only enabled for local development.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "visma-blogging-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true);
        tracing.AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }

        if (builder.Environment.IsDevelopment())
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }

        if (builder.Environment.IsDevelopment())
        {
            metrics.AddConsoleExporter();
        }
    });

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

// Swagger is intentionally available in every environment to keep the deployed API contract discoverable.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    foreach (var description in provider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint(
            $"/swagger/{description.GroupName}/swagger.json",
            $"Visma Blogging API {description.GroupName.ToUpperInvariant()}");
    }
});

// Rate limiting must run before endpoint execution so policies on controller actions are enforced.
app.UseRateLimiter();

// Liveness answers "is the process running?" and does not touch external services.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});
// Readiness answers "can this instance serve real traffic?" and includes MongoDB/RabbitMQ checks.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapControllers();

app.Run();

static string GetClientPartitionKey(HttpContext context)
{
    // Partitioning by client IP prevents one caller from consuming the whole shared rate limit.
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
}

/// <summary>
/// Test entry point for WebApplicationFactory.
/// </summary>
public partial class Program;
