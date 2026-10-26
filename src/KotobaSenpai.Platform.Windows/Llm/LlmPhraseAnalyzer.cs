using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.PhraseAnalysis;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Protocol-agnostic adapter for <see cref="ILlmPhraseAnalyzer"/>. BYOK settings are read via <see cref="ISettingsService"/>;
/// transport, protocol resolution, authentication, error mapping, cancellation/timeout live here; the request/response envelope is handed to the
/// selected <see cref="ILlmProtocol"/>. Skips the call when no key is configured. Cancellation/timeout/transport/refusal/
/// malformed JSON are all mapped to retryable diagnostics rather than thrown through the recognition flow.
/// </summary>
public sealed class LlmPhraseAnalyzer : ILlmPhraseAnalyzer
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILlmProtocol? _fixedProtocol;
    private readonly LlmProtocolRegistry _protocolRegistry;
    private readonly PhrasePromptBuilder _promptBuilder;
    private readonly PhraseResponseParser _parser;
    private readonly IDiagnosticReporter? _diagnostics;
    private readonly ILogger? _logger;

    public LlmPhraseAnalyzer(
        ISettingsService settings,
        HttpClient httpClient,
        ILlmProtocol? protocol = null,
        PhrasePromptBuilder? promptBuilder = null,
        PhraseResponseParser? parser = null,
        IDiagnosticReporter? diagnostics = null,
        ILogger? logger = null,
        LlmProtocolRegistry? protocolRegistry = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _fixedProtocol = protocol;
        _protocolRegistry = protocolRegistry ?? new LlmProtocolRegistry();
        _promptBuilder = promptBuilder ?? new PhrasePromptBuilder();
        _parser = parser ?? new PhraseResponseParser();
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<PhraseAnalysisResult> AnalyzeAsync(
        PhraseAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.GetValue(LlmSettingsKeys.ApiKey);
        var endpoint = _settings.GetValue(LlmSettingsKeys.Endpoint);
        var model = _settings.GetValue(LlmSettingsKeys.Model);
        if (!string.Equals(_settings.GetValue(LlmSettingsKeys.Enabled), "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, [], null);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, [], null);

        var protocol = _fixedProtocol ?? _protocolRegistry.Resolve(_settings.GetValue(LlmSettingsKeys.Protocol));

        string systemPrompt, userContent;
        try
        {
            (systemPrompt, userContent) = _promptBuilder.Build(request, protocol.PromptProfile);
        }
        catch (RequestTooLargeException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.InvalidResponse, [], ex.Message);
        }

        var body = protocol.BuildBody(systemPrompt, userContent, model);
        var candidates = LlmEndpointHelper.AnalysisCandidates(endpointUri, protocol.Path);
        string? envelope = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, candidate);
            try
            {
                protocol.ApplyAuthentication(httpRequest, apiKey);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.TransportError, [], null);
            }
            httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var llmTimer = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (response.IsSuccessStatusCode)
                {
                    envelope = responseBody;
                    _logger?.LogInformation("LLM {0} ({1}) returned {2} from {3} in {4} ms",
                        request.SegmentId, protocol.PromptProfile, (int)response.StatusCode,
                        candidate.GetLeftPart(UriPartial.Path), llmTimer.ElapsedMilliseconds);
                    break;
                }

                _diagnostics?.RecordLlmExchange(request.RecognitionId, request.SegmentId, body,
                    responseBody.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal), "[]", "[]");
                _logger?.LogWarning("LLM {0} ({1}) returned {2} from {3} in {4} ms",
                    request.SegmentId, protocol.PromptProfile, (int)response.StatusCode,
                    candidate.GetLeftPart(UriPartial.Path), llmTimer.ElapsedMilliseconds);

                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed
                    && index + 1 < candidates.Count)
                    continue;

                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [],
                    $"Provider refused with HTTP {(int)response.StatusCode} after trying {index + 1} endpoint(s).");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Timeout, [], null);
            }
            catch (OperationCanceledException)
            {
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Cancelled, [], null);
            }
            catch (HttpRequestException ex)
            {
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.TransportError, [], ex.Message);
            }
        }

        if (envelope is null)
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [], "No analysis endpoint accepted the request.");

        string groupsJson = "[]";
        string wordsJson = "[]";
        IReadOnlyList<ParsedPhraseGroup> groups;
        IReadOnlyList<ParsedWordMeaning> words;
        try
        {
            var groupsPayload = protocol.ExtractGroupsJson(envelope);
            groupsJson = groupsPayload.GetRawText();
            var wordsPayload = protocol.ExtractWordsJson(envelope);
            wordsJson = wordsPayload.GetRawText();
            groups = _parser.ParseGroups(groupsPayload);
            words = _parser.ParseWords(wordsPayload);
        }
        catch (Exception ex) when (ex is PhraseResponseException or JsonException or KeyNotFoundException or ArgumentNullException)
        {
            // Protocol envelope or group structure mismatch (missing field / null text / non-JSON) → retryable warning, not thrown through the recognition flow.
            _diagnostics?.RecordLlmExchange(request.RecognitionId, request.SegmentId, body, envelope, groupsJson, wordsJson);
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.MalformedJson, [], ex.Message);
        }

        // Capture the raw request/response and extracted arrays with one shared exchange stamp for offline inspection.
        _diagnostics?.RecordLlmExchange(request.RecognitionId, request.SegmentId, body, envelope, groupsJson, wordsJson);
        return new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, groups, null) { Words = words };
    }
}
