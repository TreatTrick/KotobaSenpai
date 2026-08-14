using System.Text.Json;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 严格解析 DeepSeek chat-completions 响应中的 group JSON。结构不符（缺字段、类型错误、parts 非数组）
/// 抛 <see cref="PhraseResponseException"/>；token 归属/顺序等语义校验由 Core 编排器负责。
/// 可选字段（confidence/reason）被忽略；绝不使用模型提供的偏移或原始文本做几何。
/// </summary>
public sealed class PhraseResponseParser
{
    public IReadOnlyList<ParsedPhraseGroup> Parse(string envelopeJson)
    {
        try
        {
            using var envelope = JsonDocument.Parse(envelopeJson);
            var choices = envelope.RootElement.GetProperty("choices");
            string content;
            try
            {
                content = choices[0].GetProperty("message").GetProperty("content").GetString()
                    ?? throw new PhraseResponseException("Assistant content is empty.");
            }
            catch (KeyNotFoundException)
            {
                throw new PhraseResponseException("Response lacks choices[0].message.content.");
            }
            catch (IndexOutOfRangeException)
            {
                throw new PhraseResponseException("Response has an empty choices array.");
            }

            return ParseGroups(content);
        }
        catch (JsonException ex)
        {
            throw new PhraseResponseException("Response JSON is malformed.", ex);
        }
    }

    public IReadOnlyList<ParsedPhraseGroup> ParseGroups(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJsonArray(content));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                throw new PhraseResponseException("Group payload must be a JSON array.");

            var groups = new List<ParsedPhraseGroup>();
            foreach (var element in root.EnumerateArray())
            {
                var modelGroupId = Str(element, "modelGroupId");
                var type = Str(element, "type");
                var parts = Parts(element);
                var label = Str(element, "label");
                var meaningZh = Str(element, "meaningZh");
                var grammarZh = Str(element, "grammarZh");
                groups.Add(new ParsedPhraseGroup(modelGroupId, type, parts, label, meaningZh, grammarZh));
            }
            return groups;
        }
        catch (JsonException ex)
        {
            throw new PhraseResponseException("Group payload JSON is malformed.", ex);
        }
    }

    /// <summary>
    /// 从模型输出里抠出 JSON 数组：容忍 ```json 代码块和前后缀说明文字。
    /// 取第一个 '[' 到最后一个 ']' 之间的子串；数组本身是最外层结构，字符串内出现 '[' 的边界情况
    /// 由后续 JsonDocument.Parse 兜底兜错。clean 输出时这是无副作用的空操作。
    /// </summary>
    private static string ExtractJsonArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new PhraseResponseException("No JSON array found in group content.");
        return content.Substring(start, end - start + 1);
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