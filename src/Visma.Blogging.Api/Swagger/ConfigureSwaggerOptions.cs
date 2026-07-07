using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Visma.Blogging.Api.Swagger;

/// <summary>
/// Creates one OpenAPI document per API version.
/// </summary>
public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    /// <summary>
    /// Creates the options configurator.
    /// </summary>
    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public void Configure(SwaggerGenOptions options)
    {
        // The API explorer already knows every supported API version; Swagger mirrors that list.
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(
                description.GroupName,
                new OpenApiInfo
                {
                    Title = "Visma Blogging API",
                    Version = description.ApiVersion.ToString(),
                    Description = "API for creating and reading blog posts with JSON/XML support, idempotency, MongoDB persistence, and RabbitMQ integration events."
                });
        }
    }
}
