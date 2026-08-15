using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Anthropic Messages 协议：<c>/v1/messages</c>。关 thinking（快，且 thinking 模式禁止强制工具）+ 强制唯一
/// <c>tool_use</c>（<c>return_groups</c>）承载原生结构化输出，group 数组在
/// <c>content[].tool_use.input</c>。tool 仅作结构化输出载体，不接通用 tool calling 循环。
/// </summary>
public sealed class AnthropicMessagesProtocol : ILlmProtocol
{
    public string Path => "/v1/messages";

    // ponytail: 固定上限——group JSON 输出很小，无需按请求估算；极长句段由 PromptBuilder 的体积上限兜底。
    private const int MaxTokens = 4096;

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            // 关 thinking：ARK 等端点默认开 thinking（慢，且 thinking 模式禁止强制工具）；关掉才允许强制 tool_use 且快。
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