using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Core.Llm;

/// <summary>Shared validation and conditional application; no process or provider transport logic.</summary>
public sealed class LlmConfigurationService(ISettingsService settings, ILlmConnectionDetector detector, ILlmModelCatalog catalog)
{
    private const string VerificationKey = "LlmVerifiedConfiguration";
    private static readonly string[] StatusKeys = [.. LlmConfiguration.Keys, VerificationKey];

    public LlmConfigurationStatus GetStatus()
    {
        var snapshot = settings.GetSnapshot(StatusKeys);
        var current = LlmConfiguration.FromSnapshot(snapshot);
        var endpoint = string.IsNullOrEmpty(current.ApiKey) ? current.Endpoint
            : current.Endpoint.Replace(current.ApiKey, "[REDACTED]", StringComparison.Ordinal);
        return new(current.Protocol, endpoint, current.Model, !string.IsNullOrWhiteSpace(current.ApiKey),
            snapshot[VerificationKey] == current.Fingerprint());
    }

    /// <summary>Checks the saved connection without changing its fields; only verification metadata is updated.</summary>
    public async Task<AgentApiResult> CheckCurrentAsync(CancellationToken cancellationToken = default)
    {
        var before = settings.GetSnapshot(StatusKeys);
        var current = LlmConfiguration.FromSnapshot(before);
        if (AgentApiContract.Validate("detect", new()
        {
            SchemaVersion = 1, Endpoint = current.Endpoint, ApiKey = current.ApiKey, Model = current.Model,
        }) is { } invalid)
            return invalid;
        var result = await detector.CheckAsync(current, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            return AgentApiResult.Failure("cancelled", 130);
        if (!result.Succeeded)
        {
            if (result.Code != "cancelled" && before[VerificationKey] is not null)
                settings.TryUpdate(before, new Dictionary<string, string?> { [VerificationKey] = null });
            return DetectionFailure(result, current.ApiKey);
        }
        if (!settings.TryUpdate(before, new Dictionary<string, string?> { [VerificationKey] = current.Fingerprint() }))
            return AgentApiResult.Failure("conflict", 4);
        return new() { Configuration = GetStatus() };
    }

    public async Task<AgentApiResult> ExecuteAsync(string command, AgentApiRequest request,
        CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        if (AgentApiContract.Validate(command, request) is { } invalid)
            return invalid;
        if (command == "status")
            return new() { Configuration = GetStatus() };
        if (command == "models")
            return await DiscoverModelsAsync(request, cancellationToken).ConfigureAwait(false);

        var before = settings.GetSnapshot(StatusKeys);
        progress?.Report("detecting");
        var result = await detector.DetectAsync(new(LlmProtocolIds.OpenAiResponses,
            request.Endpoint!.Trim(), request.ApiKey!.Trim(), request.Model!.Trim()), cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            return AgentApiResult.Failure("cancelled", 130);
        if (!result.Succeeded)
        {
            var current = LlmConfiguration.FromSnapshot(before);
            if (command == "configure" && result.Code != "cancelled"
                && current.Endpoint.Trim() == request.Endpoint!.Trim()
                && current.ApiKey.Trim() == request.ApiKey!.Trim() && current.Model.Trim() == request.Model!.Trim()
                && before[VerificationKey] is not null)
                settings.TryUpdate(before, new Dictionary<string, string?> { [VerificationKey] = null });
            return DetectionFailure(result, request.ApiKey!.Trim());
        }
        var verified = result.Configuration!;
        if (command == "detect")
            return new() { Configuration = new(verified.Protocol, verified.Endpoint, verified.Model, true, Verified: true) };

        progress?.Report("saving");
        if (cancellationToken.IsCancellationRequested)
            return AgentApiResult.Failure("cancelled", 130);
        var changes = new Dictionary<string, string?>(verified.ToSnapshot()) { [VerificationKey] = verified.Fingerprint() };
        if (!settings.TryUpdate(before, changes))
            return AgentApiResult.Failure("conflict", 4);
        return new() { Applied = true, Configuration = GetStatus() };
    }

    private static AgentApiResult DetectionFailure(LlmDetectionResult result, string apiKey)
    {
        apiKey = apiKey.Trim();
        var detail = result.ErrorDetail?.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(apiKey), "[REDACTED]", StringComparison.Ordinal);
        return AgentApiResult.Failure(result.Code, result.Code == "cancelled" ? 130 : 3) with { ErrorDetail = detail };
    }

    private async Task<AgentApiResult> DiscoverModelsAsync(AgentApiRequest request, CancellationToken cancellationToken)
    {
        LlmModelCatalogResult? last = null;
        // Chat and Responses share catalog authentication, so query it once before trying Anthropic.
        foreach (var protocol in new[] { LlmProtocolIds.OpenAiResponses, LlmProtocolIds.Anthropic })
        {
            last = await catalog.GetModelsAsync(new(request.Endpoint!, request.ApiKey!, protocol), cancellationToken).ConfigureAwait(false);
            if (last.Succeeded)
                return new() { Models = last.Models };
            if (last.Failure is not (LlmModelCatalogFailure.Authentication or LlmModelCatalogFailure.NotFound or LlmModelCatalogFailure.Unsupported))
                break;
        }
        return AgentApiResult.Failure(last?.Failure switch
        {
            LlmModelCatalogFailure.Authentication => "authentication_failed",
            LlmModelCatalogFailure.Timeout => "timeout",
            LlmModelCatalogFailure.Cancelled => "cancelled",
            LlmModelCatalogFailure.Empty => "no_models",
            LlmModelCatalogFailure.NotFound or LlmModelCatalogFailure.Unsupported => "catalog_unavailable",
            _ => "transport_error",
        }, last?.Failure == LlmModelCatalogFailure.Cancelled ? 130 : 3);
    }

}
