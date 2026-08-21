using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// Protocol-agnostic adapter for <see cref="ILlmPhraseAnalyzer"/>. BYOK settings are read via <see cref="ISettingsService"/>;
/// transport, Bearer auth, error mapping, cancellation/timeout live here; the request/response envelope is handed to the
/// selected <see cref="ILlmProtocol"/>. Skips the call when no key is configured. Cancellation/timeout/transport/refusal/
/// malformed JSON are all mapped to retryable diagnostics rather than thrown through the recognition flow.
/// </summary>
public sealed class LlmPhraseAnalyzer : ILlmPhraseAnalyzer
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILlmProtocol _protocol;
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
        IStringLocalizer? localizer = null,
        IDiagnosticReporter? diagnostics = null,
        ILogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _protocol = protocol ?? new OpenAiChatCompletionsProtocol();
        // ponytail: fallback only serves callers that don't inject a builder (production always injects via DI); returns the key itself when a localizer is missing.
        _promptBuilder = promptBuilder ?? new PhrasePromptBuilder(localizer ?? new KeyReturningLocalizer());
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
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, [], null);

        string systemPrompt, userContent;
        try
        {
            (systemPrompt, userContent) = _promptBuilder.Build(request, _protocol.PromptProfile);
        }
        catch (RequestTooLargeException ex)
        {
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.InvalidResponse, [], ex.Message);
        }

        var body = _protocol.BuildBody(systemPrompt, userContent, model);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + _protocol.Path);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        string envelope;
        var llmTimer = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [], $"Provider returned {(int)response.StatusCode}.");
            if (!response.IsSuccessStatusCode)
                return new PhraseAnalysisResult(PhraseAnalysisOutcome.Refused, [], $"Provider refused with {(int)response.StatusCode}.");
            envelope = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("LLM {0} returned {1} in {2} ms", request.SegmentId, (int)response.StatusCode, llmTimer.ElapsedMilliseconds);
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

        // Capture the raw request/response verbatim (request body never contains the API key) for offline inspection.
        _diagnostics?.RecordLlmExchange(request.SegmentId, body, envelope);

        try
        {
            var groups = _parser.ParseGroups(_protocol.ExtractGroupsJson(envelope));
            var words = _parser.ParseWords(_protocol.ExtractWordsJson(envelope));
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, groups, null) { Words = words };
        }
        catch (Exception ex) when (ex is PhraseResponseException or JsonException or KeyNotFoundException or ArgumentNullException)
        {
            // Protocol envelope or group structure mismatch (missing field / null text / non-JSON) → retryable warning, not thrown through the recognition flow.
            return new PhraseAnalysisResult(PhraseAnalysisOutcome.MalformedJson, [], ex.Message);
        }
    }
}

/// <summary>Fallback when no localizer is injected: returns the key name itself (only hit via tests/manual construction).</summary>
internal sealed class KeyReturningLocalizer : IStringLocalizer
{
#pragma warning disable CS0067 // Event required by the interface; never raised on the fallback path
    public event EventHandler? CultureChanged;
#pragma warning restore CS0067

    public string Get(string key, params object[] args)
        => args.Length == 0 ? key : string.Join(",", args);
}
