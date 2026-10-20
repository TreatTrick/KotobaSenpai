namespace KotobaSenpai.Core.Llm;

/// <summary>Persisted identifiers for the supported provider wire protocols.</summary>
public static class LlmProtocolIds
{
    public const string OpenAiChat = "openai-chat";
    public const string OpenAiResponses = "openai-responses";
    public const string Anthropic = "anthropic";

    public static IReadOnlyList<LlmProtocolOption> Options { get; } =
    [
        new(OpenAiChat, "Llm_ProtocolOpenAiChat"),
        new(OpenAiResponses, "Llm_ProtocolOpenAiResponses"),
        new(Anthropic, "Llm_ProtocolAnthropic"),
    ];

    public static string Normalize(string? value)
        => value switch
        {
            OpenAiResponses or "responses" => OpenAiResponses,
            Anthropic => Anthropic,
            OpenAiChat or null or "" => OpenAiChat,
            _ => OpenAiChat,
        };
}

/// <summary>Flat settings keys used by the single active LLM configuration.</summary>
public static class LlmSettingsKeys
{
    public const string ApiKey = "LlmApiKey";
    public const string Endpoint = "LlmEndpoint";
    public const string Model = "LlmModel";
    public const string Protocol = "LlmProtocol";
    public const string Enabled = "PhraseGroupsEnabled";
}

/// <summary>Display metadata for a selectable protocol.</summary>
public sealed record LlmProtocolOption(string Id, string DisplayNameKey);

/// <summary>One model returned by a provider catalog.</summary>
public sealed record LlmModelInfo(string Id, string? Owner = null);

public enum LlmModelCatalogFailure
{
    MissingEndpoint,
    MissingApiKey,
    Authentication,
    Timeout,
    Unsupported,
    Empty,
    NotFound,
    Transport,
    Cancelled,
}

public sealed record LlmModelCatalogResult(
    IReadOnlyList<LlmModelInfo> Models,
    LlmModelCatalogFailure? Failure = null,
    string? Detail = null)
{
    public bool Succeeded => Failure is null;

    public static LlmModelCatalogResult Success(IReadOnlyList<LlmModelInfo> models)
        => new(models);

    public static LlmModelCatalogResult Failed(LlmModelCatalogFailure failure, string? detail = null)
        => new(Array.Empty<LlmModelInfo>(), failure, detail);
}

/// <summary>Input for a cancellable provider model-catalog request.</summary>
public sealed record LlmModelCatalogRequest(string Endpoint, string ApiKey, string Protocol);

/// <summary>Core port for discovering models; implementations own HTTP and provider-specific parsing.</summary>
public interface ILlmModelCatalog
{
    Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default);
}
