using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Visma.Blogging.Api;

namespace Visma.Blogging.IntegrationTests;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Middleware_returns_problem_details_for_unhandled_exception()
    {
        static Task ThrowingNext(HttpContext _)
        {
            throw new InvalidOperationException("Unexpected failure");
        }

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/post/error";
        context.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionHandlingMiddleware(
            ThrowingNext,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("Unexpected server error", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("trace-123", body.RootElement.GetProperty("traceId").GetString());
    }
}
