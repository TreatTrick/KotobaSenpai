using System.Text.Json;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 严格校验结构化输出中的 group 数组。结构不符（缺字段、类型错误、parts 非数组）抛
/// <see cref="PhraseResponseException"/>；token 归属/顺序等语义校验由 Core 编排器负责。
/// 信封提取（各协议读取结构化输出位置）由 <see cref="ILlmProtocol.ExtractGroupsJson"/> 承担，
/// 这里只做字段校验。可选字段（confidence/reason）被忽略。
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
            var meaningZh = Str(element, "meaningZh");
            var grammarZh = Str(element, "grammarZh");
            result.Add(new ParsedPhraseGroup(modelGroupId, type, parts, label, meaningZh, grammarZh));
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

/// <summary>提供方响应结构不符。</summary>
public sealed class PhraseResponseException(string message, Exception? innerException = null)
    : Exception(message, innerException);