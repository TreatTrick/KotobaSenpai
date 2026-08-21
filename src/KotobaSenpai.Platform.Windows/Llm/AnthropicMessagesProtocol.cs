using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Anthropic Messages protocol: <c>/v1/messages</c>. The forced <c>return_groups</c> tool carries the structured
/// result in <c>content[].tool_use.input</c>; this shape is supported by Anthropic-compatible endpoints that do not
/// implement native <c>output_config.format</c> JSON outputs.
/// </summary>
public sealed class AnthropicMessagesProtocol : ILlmProtocol
{
    public LlmPromptProfile PromptProfile => LlmPromptProfile.AnthropicMessages;

    public string Path => "/v1/messages";

    // ponytail: fixed cap — group JSON output is tiny, no need to estimate per request; very long segments are backstopped by the PromptBuilder's size limit.
    private const int MaxTokens = 4096;

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            // Keep thinking disabled for a compact, deterministic structured response.
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = "return_groups",
                ["description"] = "Return exactly one object with the required groups and words arrays. Copy token IDs exactly from the request.",
                ["input_schema"] = PhraseGroupSchema.Root.DeepClone(),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = "return_groups" },
        };
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public JsonElement ExtractGroupsJson(string envelopeJson)
        => ExtractToolInput(envelopeJson).GetProperty("groups").Clone();

    public JsonElement ExtractWordsJson(string envelopeJson)
        => ExtractToolInput(envelopeJson).TryGetProperty("words", out var words) ? words.Clone() : EmptyArray();

    private static JsonElement ExtractToolInput(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            throw new PhraseResponseException("Response lacks a content array.");

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var type) || type.GetString() != "tool_use")
                continue;
            if (!block.TryGetProperty("name", out var name) || name.GetString() != "return_groups")
                continue;
            if (!block.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
                throw new PhraseResponseException("return_groups tool block has no object input.");
            return input.Clone();
        }
        throw new PhraseResponseException("Response lacks a return_groups tool block with structured group data.");
    }

    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();
}
