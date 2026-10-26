using System.Net;
using System.Text;
using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Platform.Windows.Llm;
using LlmSettingsKeys = KotobaSenpai.Core.Llm.LlmSettingsKeys;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class LlmEndpointFallbackTests
{
    public static TheoryData<string, string, string[]> CandidateCases => new()
    {
        { LlmProtocolIds.OpenAiChat, "https://provider.example", ["/", "/v1/chat/completions", "/chat/completions"] },
        { LlmProtocolIds.OpenAiChat, "https://provider.example/v1/", ["/v1/", "/v1/chat/completions"] },
        { LlmProtocolIds.OpenAiChat, "https://provider.example/gateway", ["/gateway", "/gateway/v1/chat/completions", "/gateway/chat/completions"] },
        { LlmProtocolIds.OpenAiChat, "https://provider.example/v1/chat/completions", ["/v1/chat/completions"] },
        { LlmProtocolIds.OpenAiChat, "https://provider.example/chat/completions", ["/chat/completions", "/v1/chat/completions"] },
        { LlmProtocolIds.OpenAiResponses, "https://provider.example", ["/", "/v1/responses", "/responses"] },
        { LlmProtocolIds.OpenAiResponses, "https://provider.example/v1/responses", ["/v1/responses"] },
        { LlmProtocolIds.Anthropic, "https://provider.example", ["/", "/v1/messages", "/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/v1", ["/v1", "/v1/messages", "/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/anthropic", ["/anthropic", "/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/anthropic/v1/", ["/anthropic/v1/", "/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/v1/messages", ["/v1/messages", "/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/anthropic/v1/messages", ["/anthropic/v1/messages"] },
        { LlmProtocolIds.Anthropic, "https://provider.example/gateway/v1", ["/gateway/v1", "/gateway/v1/messages", "/gateway/anthropic/v1/messages"] },
    };

    [Theory]
    [InlineData(LlmProtocolIds.OpenAiChat)]
    [InlineData(LlmProtocolIds.OpenAiResponses)]
    [InlineData(LlmProtocolIds.Anthropic)]
    public async Task Posts_to_the_configured_url_unchanged_and_stops_on_success(string protocol)
    {
        const string endpoint = "https://provider.example/custom/execute/?api-version=2026-01-01";
        var handler = new RecordingHandler((_, _) => Response(HttpStatusCode.OK, Envelope(protocol)));
        var analyzer = CreateAnalyzer(endpoint, protocol, handler);

        var result = await analyzer.AnalyzeAsync(Request());

        Assert.True(result.Succeeded);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(endpoint, sent.Uri.AbsoluteUri);
    }

    [Theory]
    [MemberData(nameof(CandidateCases))]
    public async Task Tries_each_unique_protocol_candidate_once_before_reporting_exhaustion(
        string protocol, string endpoint, string[] expectedPaths)
    {
        var handler = new RecordingHandler((_, _) => Response(HttpStatusCode.NotFound, ""));
        var analyzer = CreateAnalyzer(endpoint, protocol, handler);

        var result = await analyzer.AnalyzeAsync(Request());

        Assert.Equal(expectedPaths, handler.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.Equal(handler.Requests.Count, handler.Requests.Select(request => request.Uri).Distinct().Count());
        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
        Assert.Contains("404", result.Warning!);
        Assert.Contains($"{expectedPaths.Length} endpoint(s)", result.Warning!);
    }

    [Theory]
    [InlineData(LlmProtocolIds.OpenAiChat, HttpStatusCode.NotFound)]
    [InlineData(LlmProtocolIds.OpenAiResponses, HttpStatusCode.NotFound)]
    [InlineData(LlmProtocolIds.Anthropic, HttpStatusCode.NotFound)]
    [InlineData(LlmProtocolIds.OpenAiChat, HttpStatusCode.MethodNotAllowed)]
    [InlineData(LlmProtocolIds.OpenAiResponses, HttpStatusCode.MethodNotAllowed)]
    [InlineData(LlmProtocolIds.Anthropic, HttpStatusCode.MethodNotAllowed)]
    public async Task Reuses_the_body_and_protocol_authentication_on_fallback_and_stops_on_success(
        string protocol, HttpStatusCode firstStatus)
    {
        var handler = new RecordingHandler((attempt, _) => attempt == 1
            ? Response(firstStatus, "")
            : Response(HttpStatusCode.OK, Envelope(protocol)));
        var analyzer = CreateAnalyzer("https://provider.example/gateway?api-version=v1", protocol, handler);

        var result = await analyzer.AnalyzeAsync(Request());

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.All(handler.Requests, sent =>
        {
            Assert.Equal("?api-version=v1", sent.Uri.Query);
            if (protocol == LlmProtocolIds.Anthropic)
            {
                Assert.Equal("secret-api-key", sent.ApiKey);
                Assert.Equal("2023-06-01", sent.Version);
                Assert.Null(sent.Authorization);
            }
            else
            {
                Assert.Equal("Bearer secret-api-key", sent.Authorization);
                Assert.Null(sent.ApiKey);
            }
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Does_not_try_another_address_for_other_http_errors(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) => Response(status, """{"error":"Request rejected."}"""));

        var result = await CreateAnalyzer("https://provider.example", LlmProtocolIds.OpenAiChat, handler)
            .AnalyzeAsync(Request());

        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
        Assert.Single(handler.Requests);
        Assert.Contains(((int)status).ToString(), result.Warning!);
    }

    [Fact]
    public async Task Does_not_try_another_address_for_a_malformed_successful_response()
    {
        var handler = new RecordingHandler((_, _) => Response(HttpStatusCode.OK, "not JSON"));

        var result = await CreateAnalyzer("https://provider.example", LlmProtocolIds.OpenAiChat, handler)
            .AnalyzeAsync(Request());

        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cancellation_after_the_first_attempt_prevents_further_requests()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler((_, _) =>
        {
            cancellation.Cancel();
            return Response(HttpStatusCode.NotFound, "");
        });

        var result = await CreateAnalyzer("https://provider.example", LlmProtocolIds.OpenAiChat, handler)
            .AnalyzeAsync(Request(), cancellation.Token);

        Assert.Equal(PhraseAnalysisOutcome.Cancelled, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true, PhraseAnalysisOutcome.Timeout)]
    [InlineData(false, PhraseAnalysisOutcome.TransportError)]
    public async Task Does_not_try_another_address_after_transport_failure(bool timeout, PhraseAnalysisOutcome expected)
    {
        var handler = new RecordingHandler((_, _) => timeout
            ? throw new TaskCanceledException("Request timed out.")
            : throw new HttpRequestException("Connection failed."));

        var result = await CreateAnalyzer("https://provider.example", LlmProtocolIds.OpenAiChat, handler)
            .AnalyzeAsync(Request());

        Assert.Equal(expected, result.Outcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stops_when_a_later_candidate_returns_a_non_routing_error()
    {
        var handler = new RecordingHandler((attempt, _) =>
            Response(attempt == 1 ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized, ""));

        var result = await CreateAnalyzer("https://provider.example", LlmProtocolIds.OpenAiChat, handler)
            .AnalyzeAsync(Request());

        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("401", result.Warning!);
    }

    [Fact]
    public async Task Records_each_failed_attempt_and_redacts_reflected_api_keys()
    {
        var reporter = new RecordingReporter();
        var logger = new RecordingLogger();
        const string error = """{"error":{"message":"Rejected secret-api-key"}}""";
        var handler = new RecordingHandler((attempt, _) => attempt < 3
            ? Response(HttpStatusCode.NotFound, error)
            : Response(HttpStatusCode.OK, Envelope(LlmProtocolIds.OpenAiChat)));
        var analyzer = CreateAnalyzer("https://provider.example?token=secret-api-key",
            LlmProtocolIds.OpenAiChat, handler, reporter, logger);

        var result = await analyzer.AnalyzeAsync(Request());

        Assert.True(result.Succeeded);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(3, reporter.Exchanges.Count);
        Assert.All(reporter.Exchanges.Take(2), exchange => Assert.Contains("[REDACTED]", exchange.Response));
        Assert.All(reporter.Exchanges, exchange =>
        {
            Assert.DoesNotContain("secret-api-key", exchange.Request);
            Assert.DoesNotContain("secret-api-key", exchange.Response);
        });
        Assert.Equal(2, logger.Messages.Count(entry => entry.Level == LogLevel.Warning));
        Assert.Contains(logger.Messages, entry => entry.Message.Contains("/v1/chat/completions", StringComparison.Ordinal));
        Assert.All(logger.Messages, entry => Assert.DoesNotContain("secret-api-key", entry.Message));
    }

    private static LlmPhraseAnalyzer CreateAnalyzer(string endpoint, string protocol, RecordingHandler handler,
        IDiagnosticReporter? reporter = null, ILogger? logger = null)
        => new(new Settings(endpoint, protocol), new HttpClient(handler), diagnostics: reporter, logger: logger);

    private static PhraseAnalysisRequest Request()
        => new(Guid.NewGuid(), "s0", "Sample text.", [], []);

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Envelope(string protocol) => protocol switch
    {
        LlmProtocolIds.Anthropic => """{"content":[{"type":"tool_use","name":"return_groups","input":{"groups":[],"words":[]}}]}""",
        LlmProtocolIds.OpenAiResponses => """{"output":[{"type":"message","content":[{"type":"output_text","text":"{\"groups\":[],\"words\":[]}"}]}]}""",
        _ => """{"choices":[{"message":{"content":"{\"groups\":[],\"words\":[]}"}}]}""",
    };

    private sealed class Settings(string endpoint, string protocol) : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => key switch
        {
            LlmSettingsKeys.Endpoint => endpoint,
            LlmSettingsKeys.Protocol => protocol,
            LlmSettingsKeys.ApiKey => "secret-api-key",
            LlmSettingsKeys.Model => "test-model",
            _ => null,
        };

        public override void SetValue(string key, string? value) { }
    }

    private sealed record CapturedRequest(Uri Uri, string Body, string? Authorization, string? ApiKey, string? Version);

    private sealed class RecordingHandler(Func<int, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Requests.Add(new CapturedRequest(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-api-key", out var keys) ? keys.Single() : null,
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null));
            return respond(Requests.Count, cancellationToken);
        }
    }

    private sealed class RecordingReporter : IDiagnosticReporter
    {
        public List<(string Request, string Response)> Exchanges { get; } = [];
        public void RecordTokens(Guid recognitionId, WindowTarget target, IReadOnlyList<GroupedWord> groupedWords) { }
        public void RecordPhraseAnalysis(Guid recognitionId, PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning) { }
        public void RecordLlmExchange(Guid recognitionId, string segmentId, string requestJson, string responseJson, string groupsJson, string wordsJson)
            => Exchanges.Add((requestJson, responseJson));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
            => Messages.Add((level, string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args)));
    }
}
