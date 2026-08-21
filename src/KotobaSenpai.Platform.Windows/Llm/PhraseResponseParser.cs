using System.Text.Json;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Validates the group array in structured output. Structural mismatches (missing fields, wrong types, parts
/// not being an array) throw <see cref="PhraseResponseException"/>; a part element that isn't a parseable token id
/// (e.g. a surface echoed next to the id) is skipped with a warning instead of failing the parse — Core drops groups
/// that end up with empty parts. Semantic validation such as token ownership/order is the Core orchestrator's job.
/// Envelope extraction (where each protocol reads the structured output) is handled by
/// <see cref="ILlmProtocol.ExtractGroupsJson"/>; this only does field validation. Optional fields (confidence/reason)
/// are ignored.
/// </summary>
public sealed class PhraseResponseParser
{
    private readonly ILogger? _logger;

    public PhraseResponseParser(ILogger? logger = null) => _logger = logger;

    public IReadOnlyList<ParsedPhraseGroup> ParseGroups(JsonElement groups)
    {
        if (groups.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Group payload must be a JSON array.");

        var result = new List<ParsedPhraseGroup>();
        foreach (var element in groups.EnumerateArray())
        {
            var modelGroupId = Str(element, "modelGroupId");
            var type = Str(element, "type");
            var parts = Parts(element, modelGroupId);
            var label = Str(element, "label");
            var meaning = Str(element, "meaning");
            var grammar = Str(element, "grammar");
            result.Add(new ParsedPhraseGroup(modelGroupId, type, parts, label, meaning, grammar));
        }
        return result;
    }

    public IReadOnlyList<ParsedWordMeaning> ParseWords(JsonElement words)
    {
        if (words.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Words payload must be a JSON array.");

        var result = new List<ParsedWordMeaning>();
        foreach (var element in words.EnumerateArray())
        {
            var headword = Str(element, "headword");
            var pos = Str(element, "pos");
            var meaning = Str(element, "meaning");
            var grammar = Str(element, "grammar");
            result.Add(new ParsedWordMeaning(headword, pos, meaning, grammar));
        }
        return result;
    }

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new PhraseResponseException($"Group is missing string field '{name}'.");
        return value.GetString()!;
    }

    private IReadOnlyList<IReadOnlyList<SentenceTokenId>> Parts(JsonElement element, string modelGroupId)
    {
        if (!element.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Group is missing array field 'parts'.");

        var result = new List<IReadOnlyList<SentenceTokenId>>();
        var partIndex = 0;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Array)
                throw new PhraseResponseException("A part must be an array of token ids.");
            var ids = new List<SentenceTokenId>();
            foreach (var id in part.EnumerateArray())
            {
                // The schema only promises strings, and some providers echo the surface next to the token id
                // (e.g. ["l0:t6", "し"]); skip the invalid element rather than failing the whole parse.
                if (id.ValueKind != JsonValueKind.String || !SentenceTokenId.TryParse(id.GetString(), out var tokenId))
                {
                    _logger?.LogWarning(
                        "Group '{0}' part {1} token id '{2}' is invalid; must be a valid 'l{{line}}:t{{token}}' string. Skipping it.",
                        modelGroupId, partIndex, id.GetString() ?? id.ToString());
                    continue;
                }
                ids.Add(tokenId);
            }
            result.Add(ids);
            partIndex++;
        }
        return result;
    }
}

/// <summary>The provider response structure does not match what was expected.</summary>
public sealed class PhraseResponseException(string message, Exception? innerException = null)
    : Exception(message, innerException);