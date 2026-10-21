using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>OpenAI Chat Completions protocol with JSON object output and a prompt-level output example.</summary>
public sealed class OpenAiChatCompletionsProtocol : ILlmProtocol
{
    private const string OutputExample = """{"groups":[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0","l0:t1"]],"label":"example","meaning":"example","grammar":"example"}],"words":[{"headword":"example","pos":"example","meaning":"example","grammar":"example"}]}""";

    public LlmPromptProfile PromptProfile => LlmPromptProfile.OpenAiChatCompletions;

    public string Path => "/chat/completions";

    public void ApplyAuthentication(HttpRequestMessage request, string apiKey)
        => request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

    public string BuildBody(string systemPrompt, string userContent, string model)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["reasoning_effort"] = "low",
            // DeepSeek's OpenAI-compatible endpoint uses this extension to explicitly disable thinking.
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
            ["temperature"] = 0.0,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt + "\nJSON output structure example (replace all example values with the actual analysis):\n" + OutputExample },
                new JsonObject { ["role"] = "user", ["content"] = userContent }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_object",
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
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new PhraseResponseException("Response has an empty choices array.");
        var content = choices[0]
            .GetProperty("message").GetProperty("content").GetString()
            ?? throw new PhraseResponseException("Assistant content is empty.");
        using var contentDoc = JsonDocument.Parse(content);
        return contentDoc.RootElement.Clone();
    }

    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();
}
