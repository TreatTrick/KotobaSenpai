namespace KotobaSenpai.Core.Llm;

/// <summary>Versioned input for the CLI and shared configuration use case.</summary>
public sealed record AgentApiRequest
{
    public int SchemaVersion { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Language { get; init; }
    public string? ProtocolHint { get; init; }

    public override string ToString() => "Agent API request (credentials redacted)";
}

public sealed record LlmConfigurationStatus(string Protocol, string Endpoint, string Model, bool KeyConfigured, bool Verified = false);

public sealed record AgentApiResult
{
    public int SchemaVersion { get; init; } = 1;
    public Guid OperationId { get; init; } = Guid.NewGuid();
    public string Status { get; init; } = "success";
    public string Code { get; init; } = "ok";
    public int ExitCode { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = [];
    public IReadOnlyList<LlmModelInfo> Models { get; init; } = [];
    public LlmConfigurationStatus? Configuration { get; init; }
    public bool? Applied { get; init; } = false;
    public string UiRestore { get; init; } = "not_needed";
    public string? NextAction { get; init; }
    public string? ErrorDetail { get; init; }

    public static AgentApiResult Failure(string code, int exitCode = 3, params string[] missingFields) => new()
    {
        Code = code, ExitCode = exitCode, MissingFields = missingFields,
        Status = exitCode == 2 ? "input_required" : exitCode == 130 ? "cancelled" : "failed",
        NextAction = exitCode == 2 ? "Supply the missing or corrected fields and call the tool again." : "Review the error before retrying; do not repeat identical failures.",
    };
}

public static class AgentApiContract
{
    public const int SchemaVersion = 1;
    public static IReadOnlyList<string> ProtocolOrder { get; } =
        [LlmProtocolIds.OpenAiResponses, LlmProtocolIds.Anthropic, LlmProtocolIds.OpenAiChat];

    public static AgentApiResult? Validate(string command, AgentApiRequest request)
    {
        if (request.SchemaVersion != SchemaVersion)
            return AgentApiResult.Failure("unsupported_schema_version", 2, "schemaVersion");
        if (command is "status" or "capabilities")
            return null;
        if (command is not ("models" or "detect" or "configure"))
            return AgentApiResult.Failure("unknown_command", 2);
        if (string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Trim().ToUpperInvariant() is
            "YOUR_API_KEY" or "<API_KEY>" or "<YOUR_API_KEY>" or "REPLACE_ME" or "〈填写密钥〉")
            return AgentApiResult.Failure("missing_api_key", 2, "apiKey");
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return AgentApiResult.Failure("missing_endpoint", 2, "endpoint");
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return AgentApiResult.Failure("invalid_endpoint", 2, "endpoint");
        if (command != "models" && string.IsNullOrWhiteSpace(request.Model))
            return AgentApiResult.Failure("missing_model", 2, "model");
        return null;
    }
}

public sealed record LlmDetectionResult(LlmConfiguration? Configuration, string Code = "ok", string? ErrorDetail = null)
{
    public bool Succeeded => Configuration is not null;
}

/// <summary>Platform port for verifying explicit candidates with the production request pipeline.</summary>
public interface ILlmConnectionDetector
{
    Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default);
    Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default);
}
