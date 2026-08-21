using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// OpenAI Responses protocol: <c>/responses</c>, with <c>text.format</c> strict JSON schema; the group array lives in
/// <c>output[].content[].text</c>. Single-turn system+user text, no multi-turn state.
/// </summary>
public sealed class OpenAiResponsesProtocol : ILlmProtocol
{
    public LlmPromptProfile PromptProfile => LlmPromptProfile.OpenAiResponses;

    public string Path => "/responses";

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_output_tokens"] = 4096,
            ["reasoning"] = new JsonObject { ["effort"] = "none" },
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
        => ExtractContentRoot(envelopeJson).GetProperty("groups").Clone();

    public JsonElement ExtractWordsJson(string envelopeJson)
        => ExtractContentRoot(envelopeJson).TryGetProperty("words", out var words) ? words.Clone() : EmptyArray();

    private static JsonElement ExtractContentRoot(string envelopeJson)
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
                    return textDoc.RootElement.Clone();
                }
            }
        }
        throw new PhraseResponseException("Response lacks an output_text block with group data.");
    }

    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();
}
