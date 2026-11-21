using System.Text;
using System.Text.Json;

namespace KotobaSenpai.Platform.Windows.Llm;

internal static class LlmJsonExtractor
{
    public static JsonElement ExtractObject(string text, out string? recoveryNote)
    {
        recoveryNote = null;
        try
        {
            return ParseObject(text);
        }
        catch (JsonException)
        {
            if (TryUnwrapFence(text, out var inner))
            {
                recoveryNote = "Removed a Markdown code fence.";
                try
                {
                    return ParseObject(inner);
                }
                catch (JsonException)
                {
                    return ExtractWithTrailingBraces(inner, ref recoveryNote);
                }
            }

            return ExtractWithTrailingBraces(text, ref recoveryNote);
        }
    }

    private static JsonElement ParseObject(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Assistant content must be a JSON object.");
        return document.RootElement.Clone();
    }

    private static bool TryUnwrapFence(string text, out string inner)
    {
        inner = string.Empty;
        var trimmed = text.Trim();
        var firstLineEnd = trimmed.IndexOf('\n');
        var lastLineStart = trimmed.LastIndexOf('\n') + 1;
        if (firstLineEnd < 0 || lastLineStart <= firstLineEnd + 1
            || trimmed[..firstLineEnd].TrimEnd() is not ("```" or "```json")
            || trimmed[lastLineStart..].TrimStart() != "```")
            return false;

        inner = trimmed[(firstLineEnd + 1)..lastLineStart];
        return true;
    }

    private static JsonElement ExtractWithTrailingBraces(string text, ref string? recoveryNote)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Assistant content must begin with a JSON object.");

        using var document = JsonDocument.ParseValue(ref reader);
        var extraBraces = 0;
        foreach (var value in bytes.AsSpan((int)reader.BytesConsumed))
        {
            if (value == (byte)'}')
                extraBraces++;
            else if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                throw new JsonException("Assistant JSON object is followed by unexpected content.");
        }
        if (extraBraces == 0)
            throw new JsonException("Assistant content does not contain recoverable JSON.");

        var note = $"Removed {extraBraces} extra closing brace(s) after the JSON object.";
        recoveryNote = recoveryNote is null ? note : $"{recoveryNote} {note}";
        return document.RootElement.Clone();
    }
}
