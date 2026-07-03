namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Validates an input model without performing side effects.
/// </summary>
public interface IValidator<in T>
{
    /// <summary>
    /// Validates the supplied instance.
    /// </summary>
    ValidationResult Validate(T instance);
}
