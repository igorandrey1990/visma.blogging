namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Error categories returned by application operations.
/// </summary>
public enum ErrorType
{
    /// <summary>
    /// The input is invalid.
    /// </summary>
    Validation,

    /// <summary>
    /// The requested resource does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested operation conflicts with current state.
    /// </summary>
    Conflict
}

/// <summary>
/// Application error with optional field-level details.
/// </summary>
public sealed record ApplicationError(
    ErrorType Type,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]> Details)
{
    /// <summary>
    /// Creates a validation error.
    /// </summary>
    public static ApplicationError Validation(IReadOnlyDictionary<string, string[]> details)
    {
        return new ApplicationError(ErrorType.Validation, "validation_failed", "One or more validation errors occurred.", details);
    }

    /// <summary>
    /// Creates a not found error.
    /// </summary>
    public static ApplicationError NotFound(string code, string message)
    {
        return new ApplicationError(ErrorType.NotFound, code, message, new Dictionary<string, string[]>());
    }

    /// <summary>
    /// Creates a conflict error.
    /// </summary>
    public static ApplicationError Conflict(string code, string message)
    {
        return new ApplicationError(ErrorType.Conflict, code, message, new Dictionary<string, string[]>());
    }
}

/// <summary>
/// Explicit success/failure result for application boundaries.
/// </summary>
public sealed class Result<T>
{
    private Result(T? value, ApplicationError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>
    /// True when the operation succeeded.
    /// </summary>
    // Success is derived from the absence of an error, keeping the object in one consistent state.
    public bool IsSuccess => Error is null;

    /// <summary>
    /// Successful value.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Failure information.
    /// </summary>
    public ApplicationError? Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<T> Success(T value) => new(value, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static Result<T> Failure(ApplicationError error) => new(default, error);
}
