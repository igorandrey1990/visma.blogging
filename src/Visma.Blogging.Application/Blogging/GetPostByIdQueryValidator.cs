using Visma.Blogging.Application.Abstractions;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Validates post lookup input.
/// </summary>
public sealed class GetPostByIdQueryValidator : IValidator<GetPostByIdQuery>
{
    /// <inheritdoc />
    public ValidationResult Validate(GetPostByIdQuery instance)
    {
        var result = new ValidationResult();
        if (instance.Id == Guid.Empty)
        {
            result.Add("id", "id must not be empty.");
        }

        return result;
    }
}
