using System.Text.Json;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Port for the provider's wire protocol: covers only the three real differences — the POST target path, the request-body
/// envelope (including each provider's structured-output declaration), and the <see cref="JsonElement"/> of the group
/// array extracted from the response envelope. HTTP, Bearer auth, error mapping, cancellation/timeout all stay in the
/// transport layer (<see cref="DeepSeekPhraseAnalyzer"/>); the semantic content is built by
/// <see cref="PhrasePromptBuilder"/>.
/// </summary>
public interface ILlmProtocol
{
    /// <summary>POST target relative path (appended to the configured endpoint).</summary>
    /// <example>OpenAI uses <c>/chat/completions</c>, Anthropic uses <c>/v1/messages</c>.</example>
    string Path { get; }

    /// <summary>Wraps the shared semantic prompt into the protocol's envelope (including the structured-output declaration) and serializes it as the request body.</summary>
    /// <example>
    /// The body produced by OpenAI Chat Completions looks like:
    /// <code>
    /// { "model": "…", "temperature": 0.0,
    ///   "messages": [ { "role": "system", "content": systemPrompt },
    ///                 { "role": "user",   "content": userContent } ],
    ///   "response_format": { "type": "json_schema",
    ///     "json_schema": { "name": "return_groups", "schema": …, "strict": true } } }
    /// </code>
    /// Anthropic instead uses <c>tools[0].input_schema</c> + a forced <c>tool_choice</c>; the group array lands in the
    /// response's <c>content[].tool_use.input.groups</c>.
    /// </example>
    string BuildBody(string systemPrompt, string userContent, string model);

    /// <summary>Extracts the root element of the structured group array from the response envelope. Throws <see cref="PhraseResponseException"/> on structural mismatch.</summary>
    /// <example>
    /// OpenAI Chat Completions envelope: <c>choices[0].message.content</c> is an embedded JSON string whose root
    /// <c>groups</c> is the target. Anthropic is extracted like this:
    /// <code>
    /// content[].tool_use.input.groups
    /// </code>
    /// </example>
    JsonElement ExtractGroupsJson(string envelopeJson);

    /// <summary>Extracts the <c>words</c> array from the same content root as <see cref="ExtractGroupsJson"/>. Returns an empty array when the provider omits it (tolerated for backward compatibility).</summary>
    JsonElement ExtractWordsJson(string envelopeJson);
}