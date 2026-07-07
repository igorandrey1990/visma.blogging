using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Visma.Blogging.Api.Health;

/// <summary>
/// Writes health check results as a small JSON document suitable for humans and probes.
/// </summary>
internal static class HealthCheckResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        // Keep the payload stable and compact so Docker/Kubernetes probes and people can read the same endpoint.
        var payload = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration
            })
        };

        await JsonSerializer.SerializeAsync(
                context.Response.Body,
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                context.RequestAborted)
            .ConfigureAwait(false);
    }
}
