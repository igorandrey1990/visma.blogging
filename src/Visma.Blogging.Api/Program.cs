using System.Text.Json;
using Visma.Blogging.Api;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Infrastructure;
using Visma.Blogging.Infrastructure.Logging;
using Visma.Blogging.Infrastructure.Messaging;
using Visma.Blogging.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .AddXmlSerializerFormatters();

var mongoOptions = builder.Configuration.GetSection(MongoBlogStoreOptions.SectionName).Get<MongoBlogStoreOptions>()
    ?? new MongoBlogStoreOptions();
var rabbitMqOptions = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
    ?? new RabbitMqOptions();

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
builder.Services.AddSingleton<IPostCreationStore>(provider => provider.GetRequiredService<MongoBlogStore>());
builder.Services.AddSingleton<IPostQueryStore>(provider => provider.GetRequiredService<MongoBlogStore>());
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton<IValidator<CreatePostCommand>, CreatePostCommandValidator>();
builder.Services.AddSingleton<IValidator<GetPostByIdQuery>, GetPostByIdQueryValidator>();
builder.Services.AddScoped<ICommandHandler<CreatePostCommand, PostResponse>, CreatePostCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetPostByIdQuery, PostResponse>, GetPostByIdQueryHandler>();
builder.Services.AddScoped<CreatePostEndpointService>();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

app.MapControllers();

app.Run();

/// <summary>
/// Test entry point for WebApplicationFactory.
/// </summary>
public partial class Program;
