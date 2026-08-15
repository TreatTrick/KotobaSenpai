using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Anthropic Messages protocol: <c>/v1/messages</c>. Disables thinking (fast, and thinking mode forbids forced tools) and
/// forces a single <c>tool_use</c> (<c>return_groups</c>) to carry native structured output; the group array lives in
/// <c>content[].tool_use.input</c>. The tool is only a structured-output carrier, not a general tool-calling loop.
/// </summary>
public sealed class AnthropicMessagesProtocol : ILlmProtocol
{
    public string Path => "/v1/messages";

    // ponytail: fixed cap — group JSON output is tiny, no need to estimate per request; very long segments are backstopped by the PromptBuilder's size limit.
    private const int MaxTokens = 4096;

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            // Disable thinking: endpoints like ARK enable thinking by default (slow, and thinking mode forbids forced tools); turning it off allows a forced tool_use and is fast.
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = "return_groups",
                ["description"] = "Return the phrase group array.",
                ["input_schema"] = PhraseGroupSchema.Root.DeepClone(),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = "return_groups" },
        };
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public JsonElement ExtractGroupsJson(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                return block.GetProperty("input").GetProperty("groups").Clone();
        }
        throw new PhraseResponseException("Response lacks a tool_use block with group data.");
    }
}