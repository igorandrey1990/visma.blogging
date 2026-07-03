using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Visma.Blogging.Api;

namespace Visma.Blogging.IntegrationTests;

/// <summary>
/// Tests the global exception middleware directly.
/// This is cheaper than constructing a special failing controller, and it proves the
/// middleware contract: unexpected exceptions become safe ProblemDetails responses.
/// </summary>
public sealed class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Middleware_returns_problem_details_for_unhandled_exception()
    {
        // The next delegate simulates anything later in the ASP.NET Core pipeline failing:
        // controller, model binding, infrastructure call, or another middleware.
        static Task ThrowingNext(HttpContext _)
        {
            throw new InvalidOperationException("Unexpected failure");
        }

        var context = new DefaultHttpContext();
        // We set a known trace id so the assertion can prove clients receive an id
        // they can use when asking operators to search logs.
        context.TraceIdentifier = "trace-123";
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/post/error";
        context.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionHandlingMiddleware(
            ThrowingNext,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        // The middleware writes JSON into the response stream. Resetting the position
        // lets the test read what a real HTTP client would receive.
        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("Unexpected server error", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("trace-123", body.RootElement.GetProperty("traceId").GetString());
    }
}
