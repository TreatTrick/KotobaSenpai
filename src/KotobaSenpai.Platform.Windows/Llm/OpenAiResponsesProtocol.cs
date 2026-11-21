using System.Net.Http;
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

    public void ApplyAuthentication(HttpRequestMessage request, string apiKey)
        => request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

    public string BuildBody(string systemPrompt, string userContent, string model, bool includeSentenceTranslation = true)
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
                    ["schema"] = PhraseGroupSchema.ForRequest(includeSentenceTranslation),
                    ["strict"] = true,
                },
            },
        };
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public JsonElement ExtractGroupsJson(string envelopeJson)
        => ExtractContentRoot(envelopeJson, out _).GetProperty("groups");

    public JsonElement ExtractWordsJson(string envelopeJson)
        => GetWords(ExtractContentRoot(envelopeJson, out _));

    public string? ExtractTranslation(string envelopeJson)
        => PhraseResponseParser.ParseTranslation(ExtractContentRoot(envelopeJson, out _));

    public (JsonElement Groups, JsonElement Words, string? Translation, string? RecoveryNote) ExtractPayload(
        string envelopeJson, bool includeSentenceTranslation)
    {
        var root = ExtractContentRoot(envelopeJson, out var recoveryNote);
        return (root.GetProperty("groups"), GetWords(root),
            includeSentenceTranslation ? PhraseResponseParser.ParseTranslation(root) : null, recoveryNote);
    }

    private static JsonElement GetWords(JsonElement root)
        => root.TryGetProperty("words", out var words) ? words : JsonSerializer.SerializeToElement(Array.Empty<object>());

    private static JsonElement ExtractContentRoot(string envelopeJson, out string? recoveryNote)
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
                    var text = block.GetProperty("text").GetString()
                        ?? throw new PhraseResponseException("Assistant content is empty.");
                    return LlmJsonExtractor.ExtractObject(text, out recoveryNote);
                }
            }
        }
        throw new PhraseResponseException("Response lacks an output_text block with group data.");
    }

}
