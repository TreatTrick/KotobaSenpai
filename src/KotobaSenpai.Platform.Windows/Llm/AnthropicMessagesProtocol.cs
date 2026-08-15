using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Anthropic Messages 协议：<c>/v1/messages</c>。追求快——关闭 thinking、不用工具调用，纯文本返回 JSON。
/// group 数组在 <c>content[].text</c>（从首 <c>[</c> 到末 <c>]</c> 抠出，容忍代码块/前后缀）。
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
            // thinking 关闭：ARK 等端点默认开 thinking（慢，且 thinking 模式禁止强制工具），关掉即快又绕开工具调用。
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userContent }),
        };
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public JsonElement ExtractGroupsJson(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        var text = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text")
                text.Append(block.GetProperty("text").GetString());
        }
        if (text.Length == 0)
            throw new PhraseResponseException("Response has no assistant text.");

        using var textDoc = JsonDocument.Parse(ExtractArray(text.ToString()));
        return textDoc.RootElement.Clone();
    }

    /// <summary>从模型文本里抠出 JSON 数组：容忍 ```json 代码块和前后缀说明文字。</summary>
    private static string ExtractArray(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new PhraseResponseException("No JSON array found in assistant text.");
        return content[start..(end + 1)];
    }
}