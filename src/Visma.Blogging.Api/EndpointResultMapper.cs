using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Api;

internal static class EndpointResultMapper
{
    public static ActionResult<PostResponse> ToPostCreatedResult(
        this CreatePostEndpointResult result,
        ControllerBase controller)
    {
        return result.Kind switch
        {
            CreatePostEndpointResultKind.Created => controller.Created(result.Location!, result.Response!),
            CreatePostEndpointResultKind.Replayed => CreateReplayedResult(result, controller),
            CreatePostEndpointResultKind.ApplicationFailure => ToPostCreatedResult(
                Result<PostResponse>.Failure(result.ApplicationError!),
                controller),
            CreatePostEndpointResultKind.BadRequest => controller.BadRequest(CreateProblem(
                result.ProblemTitle!,
                result.ProblemType!,
                StatusCodes.Status400BadRequest)),
            CreatePostEndpointResultKind.Conflict => controller.Conflict(CreateProblem(
                result.ProblemTitle!,
                result.ProblemType!,
                StatusCodes.Status409Conflict)),
            _ => throw new InvalidOperationException($"Unsupported create-post endpoint result '{result.Kind}'.")
        };
    }

    public static ActionResult<PostResponse> ToPostCreatedResult(
        this Result<PostResponse> result,
        ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            var value = result.Value!;
            return controller.Created($"/post/{value.Id:D}", value);
        }

        // Expected application failures are translated at the API boundary.
        // Handlers stay independent from ASP.NET Core result types.
        return result.Error!.Type switch
        {
            ErrorType.Validation => controller.BadRequest(CreateValidationProblem(result.Error)),
            ErrorType.Conflict => controller.Conflict(CreateProblem(result.Error, StatusCodes.Status409Conflict)),
            _ => throw new InvalidOperationException($"Unsupported error type '{result.Error.Type}'.")
        };
    }

    public static ActionResult<PostResponse> ToGetPostResult(
        this Result<PostResponse> result,
        ControllerBase controller)
    {
        if (result.IsSuccess)
        {
            return controller.Ok(result.Value!);
        }

        // A missing post is part of normal API behavior, so it maps to 404.
        return result.Error!.Type switch
        {
            ErrorType.Validation => controller.BadRequest(CreateValidationProblem(result.Error)),
            ErrorType.NotFound => controller.NotFound(CreateProblem(result.Error, StatusCodes.Status404NotFound)),
            _ => throw new InvalidOperationException($"Unsupported error type '{result.Error.Type}'.")
        };
    }

    private static ValidationProblemDetails CreateValidationProblem(ApplicationError error)
    {
        var modelState = new ModelStateDictionary();
        foreach (var fieldErrors in error.Details)
        {
            foreach (var message in fieldErrors.Value)
            {
                modelState.AddModelError(fieldErrors.Key, message);
            }
        }

        return new ValidationProblemDetails(modelState)
        {
            Title = error.Message,
            Status = StatusCodes.Status400BadRequest,
            Type = error.Code
        };
    }

    private static ProblemDetails CreateProblem(ApplicationError error, int statusCode)
    {
        return CreateProblem(error.Message, error.Code, statusCode);
    }

    private static ProblemDetails CreateProblem(string title, string type, int statusCode)
    {
        return new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Type = type
        };
    }

    private static ActionResult<PostResponse> CreateReplayedResult(
        CreatePostEndpointResult result,
        ControllerBase controller)
    {
        controller.Response.Headers["Idempotency-Replayed"] = "true";
        return controller.Created(result.Location!, result.Response!);
    }
}
