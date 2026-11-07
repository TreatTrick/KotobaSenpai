namespace KotobaSenpai.Core.Llm;

/// <summary>
/// Machine-readable outcome of a configuration command or a provider probe. The wire value
/// (see <see cref="AgentResultCodes.ToWire"/>) is part of the agent CLI contract and is also shown in
/// user-facing diagnostics, so it must stay stable; the enum is what the code compares against.
/// </summary>
public enum AgentResultCode
{
    /// <summary>The operation succeeded.</summary>
    Ok,

    /// <summary>The CLI was invoked with an unknown command line.</summary>
    InvalidCommand,

    /// <summary>The JSON input named a command the contract does not expose.</summary>
    UnknownCommand,

    /// <summary>The stdin payload was not valid JSON.</summary>
    InvalidJson,

    /// <summary>The stdin payload exceeded the accepted size.</summary>
    InputTooLarge,

    /// <summary>Reading stdin was cancelled.</summary>
    InputCancelled,

    /// <summary>Reading stdin failed.</summary>
    InputFailed,

    /// <summary>The CLI process failed before producing a result.</summary>
    CliFailed,

    /// <summary>The request used an unsupported schema version.</summary>
    UnsupportedSchemaVersion,

    /// <summary>No usable API key was supplied.</summary>
    MissingApiKey,

    /// <summary>No endpoint was supplied.</summary>
    MissingEndpoint,

    /// <summary>The endpoint is not an absolute http(s) URL.</summary>
    InvalidEndpoint,

    /// <summary>No model was supplied for a command that requires one.</summary>
    MissingModel,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The settings changed underneath an optimistic update.</summary>
    Conflict,

    /// <summary>The GUI could not be closed in time, or another owner holds the settings lock.</summary>
    AppBusy,

    /// <summary>Closing or restoring the GUI failed.</summary>
    GuiLifecycleFailed,

    /// <summary>Persisting settings failed.</summary>
    StorageFailed,

    /// <summary>The configuration callback failed for an unclassified reason.</summary>
    ConfigurationFailed,

    /// <summary>The provider rejected the credentials.</summary>
    AuthenticationFailed,

    /// <summary>The provider did not answer in time.</summary>
    Timeout,

    /// <summary>The provider could not be reached.</summary>
    TransportError,

    /// <summary>The provider rate-limited the request.</summary>
    RateLimited,

    /// <summary>The provider is unavailable (5xx).</summary>
    ProviderUnavailable,

    /// <summary>The provider answered with a redirect.</summary>
    RedirectRejected,

    /// <summary>The endpoint speaks a protocol this client does not implement.</summary>
    IncompatibleProtocol,

    /// <summary>No supported protocol was found for the endpoint.</summary>
    UnsupportedProtocol,

    /// <summary>The provider answered, but the payload contained no valid analysis.</summary>
    InvalidAnalysis,

    /// <summary>The provider returned no models.</summary>
    NoModels,

    /// <summary>The model catalog endpoint is unavailable.</summary>
    CatalogUnavailable,
}

public static class AgentResultCodes
{
    /// <summary>Wire value emitted in JSON and appended to user-facing diagnostics.</summary>
    public static string ToWire(this AgentResultCode code) => code switch
    {
        AgentResultCode.Ok => "ok",
        AgentResultCode.InvalidCommand => "invalid_command",
        AgentResultCode.UnknownCommand => "unknown_command",
        AgentResultCode.InvalidJson => "invalid_json",
        AgentResultCode.InputTooLarge => "input_too_large",
        AgentResultCode.InputCancelled => "input_cancelled",
        AgentResultCode.InputFailed => "input_failed",
        AgentResultCode.CliFailed => "cli_failed",
        AgentResultCode.UnsupportedSchemaVersion => "unsupported_schema_version",
        AgentResultCode.MissingApiKey => "missing_api_key",
        AgentResultCode.MissingEndpoint => "missing_endpoint",
        AgentResultCode.InvalidEndpoint => "invalid_endpoint",
        AgentResultCode.MissingModel => "missing_model",
        AgentResultCode.Cancelled => "cancelled",
        AgentResultCode.Conflict => "conflict",
        AgentResultCode.AppBusy => "app_busy",
        AgentResultCode.GuiLifecycleFailed => "gui_lifecycle_failed",
        AgentResultCode.StorageFailed => "storage_failed",
        AgentResultCode.ConfigurationFailed => "configuration_failed",
        AgentResultCode.AuthenticationFailed => "authentication_failed",
        AgentResultCode.Timeout => "timeout",
        AgentResultCode.TransportError => "transport_error",
        AgentResultCode.RateLimited => "rate_limited",
        AgentResultCode.ProviderUnavailable => "provider_unavailable",
        AgentResultCode.RedirectRejected => "redirect_rejected",
        AgentResultCode.IncompatibleProtocol => "incompatible_protocol",
        AgentResultCode.UnsupportedProtocol => "unsupported_protocol",
        AgentResultCode.InvalidAnalysis => "invalid_analysis",
        AgentResultCode.NoModels => "no_models",
        AgentResultCode.CatalogUnavailable => "catalog_unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unmapped agent result code."),
    };

    /// <summary>Parses a wire value; unknown values return null so callers can surface their own error.</summary>
    public static AgentResultCode? TryParseWire(string? wire) => wire switch
    {
        "ok" => AgentResultCode.Ok,
        "invalid_command" => AgentResultCode.InvalidCommand,
        "unknown_command" => AgentResultCode.UnknownCommand,
        "invalid_json" => AgentResultCode.InvalidJson,
        "input_too_large" => AgentResultCode.InputTooLarge,
        "input_cancelled" => AgentResultCode.InputCancelled,
        "input_failed" => AgentResultCode.InputFailed,
        "cli_failed" => AgentResultCode.CliFailed,
        "unsupported_schema_version" => AgentResultCode.UnsupportedSchemaVersion,
        "missing_api_key" => AgentResultCode.MissingApiKey,
        "missing_endpoint" => AgentResultCode.MissingEndpoint,
        "invalid_endpoint" => AgentResultCode.InvalidEndpoint,
        "missing_model" => AgentResultCode.MissingModel,
        "cancelled" => AgentResultCode.Cancelled,
        "conflict" => AgentResultCode.Conflict,
        "app_busy" => AgentResultCode.AppBusy,
        "gui_lifecycle_failed" => AgentResultCode.GuiLifecycleFailed,
        "storage_failed" => AgentResultCode.StorageFailed,
        "configuration_failed" => AgentResultCode.ConfigurationFailed,
        "authentication_failed" => AgentResultCode.AuthenticationFailed,
        "timeout" => AgentResultCode.Timeout,
        "transport_error" => AgentResultCode.TransportError,
        "rate_limited" => AgentResultCode.RateLimited,
        "provider_unavailable" => AgentResultCode.ProviderUnavailable,
        "redirect_rejected" => AgentResultCode.RedirectRejected,
        "incompatible_protocol" => AgentResultCode.IncompatibleProtocol,
        "unsupported_protocol" => AgentResultCode.UnsupportedProtocol,
        "invalid_analysis" => AgentResultCode.InvalidAnalysis,
        "no_models" => AgentResultCode.NoModels,
        "catalog_unavailable" => AgentResultCode.CatalogUnavailable,
        _ => null,
    };
}
