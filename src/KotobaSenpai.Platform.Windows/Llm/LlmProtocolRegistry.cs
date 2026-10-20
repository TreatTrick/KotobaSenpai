using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Resolves the active wire protocol from the persisted setting at request time.</summary>
public sealed class LlmProtocolRegistry
{
    private readonly IReadOnlyDictionary<string, ILlmProtocol> _protocols = new Dictionary<string, ILlmProtocol>(StringComparer.Ordinal)
    {
        [LlmProtocolIds.OpenAiChat] = new OpenAiChatCompletionsProtocol(),
        [LlmProtocolIds.OpenAiResponses] = new OpenAiResponsesProtocol(),
        [LlmProtocolIds.Anthropic] = new AnthropicMessagesProtocol(),
    };

    public ILlmProtocol Resolve(string? persistedValue)
        => _protocols.TryGetValue(LlmProtocolIds.Normalize(persistedValue), out var protocol)
            ? protocol
            : _protocols[LlmProtocolIds.OpenAiChat];

    public IReadOnlyList<LlmProtocolOption> Options => LlmProtocolIds.Options;
}
