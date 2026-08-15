using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// OpenAI Responses 协议：<c>/responses</c>，<c>text.format</c> strict JSON schema，group 数组在
/// <c>output[].content[].text</c>。单轮 system+user 文本，无需多轮状态。
/// </summary>
public sealed class OpenAiResponsesProtocol : ILlmProtocol
{
    public string Path => "/responses";

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "return_groups",
                    ["schema"] = PhraseGroupSchema.Root.DeepClone(),
                    ["strict"] = true,
                },
            },
        };
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public JsonElement ExtractGroupsJson(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        foreach (var item in doc.RootElement.GetProperty("output").EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "message")
                continue;
            foreach (var block in item.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var blockType) && blockType.GetString() == "output_text")
                {
                    using var textDoc = JsonDocument.Parse(block.GetProperty("text").GetString()!);
                    return textDoc.RootElement.GetProperty("groups").Clone();
                }
            }
        }
        throw new PhraseResponseException("Response lacks an output_text block with group data.");
    }
}