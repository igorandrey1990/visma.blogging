namespace Visma.Blogging.Domain;

internal static class Guard
{
    public static Guid NonEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException(field, $"{field} must not be empty.");
        }

        return value;
    }

    public static string RequiredText(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainValidationException(field, $"{field} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(field, $"{field} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }
}
