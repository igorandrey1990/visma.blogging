using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Visma.Blogging.Api;

/// <summary>
/// Converts unexpected exceptions into consistent problem responses.
/// </summary>
public sealed class GlobalExceptionHandlingMiddleware
{
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates the middleware.
    /// </summary>
    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Handles the current request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var traceId = context.TraceIdentifier;
            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                traceId);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Title = "Unexpected server error",
                Detail = "An unexpected error occurred while processing the request.",
                Status = StatusCodes.Status500InternalServerError,
                Type = "unexpected_error",
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = traceId;

            await JsonSerializer.SerializeAsync(
                    context.Response.Body,
                    problem,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
    }
}
