using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Visma.Blogging.Api.Swagger;

/// <summary>
/// Documents the optional Idempotency-Key header for POST /post operations.
/// </summary>
public sealed class IdempotencyHeaderOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Idempotency only applies to mutating create operations, not reads.
        if (!string.Equals(context.ApiDescription.HttpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional key used to safely retry POST /post. Same key and same body replays the original response; same key and different body returns 409 Conflict.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                MaxLength = 200
            }
        });
    }
}
