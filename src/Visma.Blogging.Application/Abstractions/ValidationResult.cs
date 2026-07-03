namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Field validation result.
/// </summary>
public sealed class ValidationResult
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    /// <summary>
    /// True when no errors have been recorded.
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Adds an error for a field.
    /// </summary>
    public void Add(string field, string message)
    {
        // Store all messages for a field so callers can fix multiple issues in one request.
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors.Add(field, messages);
        }

        messages.Add(message);
    }

    /// <summary>
    /// Converts the validation result to an immutable error dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ToDictionary()
    {
        return _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
