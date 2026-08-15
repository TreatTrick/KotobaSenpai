using System.Text.Json;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Strictly validates the group array in structured output. Structural mismatches (missing fields, wrong types, parts
/// not being an array) throw <see cref="PhraseResponseException"/>; semantic validation such as token ownership/order is
/// the Core orchestrator's job. Envelope extraction (where each protocol reads the structured output) is handled by
/// <see cref="ILlmProtocol.ExtractGroupsJson"/>; this only does field validation. Optional fields (confidence/reason)
/// are ignored.
/// </summary>
public sealed class PhraseResponseParser
{
    public IReadOnlyList<ParsedPhraseGroup> ParseGroups(JsonElement groups)
    {
        if (groups.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Group payload must be a JSON array.");

        var result = new List<ParsedPhraseGroup>();
        foreach (var element in groups.EnumerateArray())
        {
            var modelGroupId = Str(element, "modelGroupId");
            var type = Str(element, "type");
            var parts = Parts(element);
            var label = Str(element, "label");
            var meaning = Str(element, "meaning");
            var grammar = Str(element, "grammar");
            result.Add(new ParsedPhraseGroup(modelGroupId, type, parts, label, meaning, grammar));
        }
        return result;
    }

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new PhraseResponseException($"Group is missing string field '{name}'.");
        return value.GetString()!;
    }

    private static IReadOnlyList<IReadOnlyList<SentenceTokenId>> Parts(JsonElement element)
    {
        if (!element.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Group is missing array field 'parts'.");

        var result = new List<IReadOnlyList<SentenceTokenId>>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Array)
                throw new PhraseResponseException("A part must be an array of token ids.");
            var ids = new List<SentenceTokenId>();
            foreach (var id in part.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String || !SentenceTokenId.TryParse(id.GetString(), out var tokenId))
                    throw new PhraseResponseException("A part token id must be a valid 'l{line}:t{token}' string.");
                ids.Add(tokenId);
            }
            result.Add(ids);
        }
        return result;
    }
}

/// <summary>The provider response structure does not match what was expected.</summary>
public sealed class PhraseResponseException(string message, Exception? innerException = null)
    : Exception(message, innerException);