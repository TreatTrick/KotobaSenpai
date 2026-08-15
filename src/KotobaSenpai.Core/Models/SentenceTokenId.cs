namespace KotobaSenpai.Core.Models;

/// <summary>
/// Value object for token reference ids: a strongly typed wrapper around the <c>l{LineId}:t{LineTokenIndex}</c>
/// symbol to avoid string drift. The wire format matches the LLM contract {@code l0:t3}, but the application
/// always uses this type internally, with formatting/parsing centralized in this one place.
/// </summary>
public readonly record struct SentenceTokenId(int LineId, int LineTokenIndex)
{
    /// <summary>Wire format: <c>l0:t3</c>.</summary>
    public override string ToString() => $"l{LineId}:t{LineTokenIndex}";

    public static SentenceTokenId Parse(string value)
    {
        if (!TryParse(value, out var id))
            throw new FormatException($"Invalid token id '{value}'.");
        return id;
    }

    /// <summary>Parses <c>l{line}:t{token}</c>; returns false when the format doesn't match or an index is negative.</summary>
    public static bool TryParse(string? value, out SentenceTokenId id)
    {
        id = default;
        if (value is null || value.Length < 4 || value[0] != 'l')
            return false;
        var colon = value.IndexOf(':');
        if (colon < 2 || colon + 2 >= value.Length || value[colon + 1] != 't')
            return false;
        if (!int.TryParse(value.AsSpan(1, colon - 1), out var line) ||
            !int.TryParse(value.AsSpan(colon + 2), out var token) ||
            line < 0 || token < 0)
        {
            return false;
        }
        id = new SentenceTokenId(line, token);
        return true;
    }
}