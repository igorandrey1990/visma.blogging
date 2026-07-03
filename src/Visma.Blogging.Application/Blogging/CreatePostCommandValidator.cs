using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Validates post creation input before domain construction.
/// </summary>
public sealed class CreatePostCommandValidator : IValidator<CreatePostCommand>
{
    /// <inheritdoc />
    public ValidationResult Validate(CreatePostCommand instance)
    {
        var result = new ValidationResult();

        RequireText(result, instance.Title, "title", Post.TitleMaxLength);
        RequireText(result, instance.Description, "description", Post.DescriptionMaxLength);
        RequireText(result, instance.Content, "content", Post.ContentMaxLength);

        if (instance.Author is null)
        {
            result.Add("author", "author is required.");
            return result;
        }

        RequireText(result, instance.Author.Name, "author.name", Author.NameMaxLength);
        RequireText(result, instance.Author.Surname, "author.surname", Author.NameMaxLength);

        return result;
    }

    private static void RequireText(ValidationResult result, string? value, string field, int maxLength)
    {
        // Validate the trimmed value because the domain stores normalized text.
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            result.Add(field, $"{field} is required.");
            return;
        }

        if (trimmed.Length > maxLength)
        {
            result.Add(field, $"{field} must not exceed {maxLength} characters.");
        }
    }
}
