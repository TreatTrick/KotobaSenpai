using System.Net;
using System.Text;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhraseRequestBuilderTests
{
    private readonly PhraseRequestBuilder _builder = new();

    [Fact]
    public void Body_contains_token_ids_and_metadata_but_no_key_or_offsets()
    {
        var body = _builder.BuildBody(Request(Segment("あ")), "deepseek-chat");

        Assert.Contains("l0:t0", body);
        Assert.DoesNotContain("apiKey", body);
        Assert.DoesNotContain("screenshot", body);
        Assert.DoesNotContain("offset", body);
    }

    [Fact]
    public void Body_rejects_oversized_segment_text()
    {
        var large = new string('あ', 20_000);
        Assert.Throws<RequestTooLargeException>(() => _builder.BuildBody(Request(Segment(large)), "deepseek-chat"));
    }

    private static SentenceTokenReference Ref()
        => new(0, 0, 0, 0, Token("あ"), [new ScreenRect(0, 0, 10, 20)]);

    private static Token Token(string surface)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("pos1", "", "", ""), "cType", "cForm", "", 0);

    private static PhraseAnalysisRequest Request(string text)
        => new("s0", text, [Ref()], [new LocalSpanSummary(text, text, text, [SentenceTokenId.Parse("l0:t0")])]);

    private static string Segment(string text) => text;
}

public sealed class PhraseResponseParserTests
{
    private readonly PhraseResponseParser _parser = new();

    [Fact]
    public void Parses_multi_part_and_cross_line_groups()
    {
        var json = """
        {"choices":[{"message":{"content":"[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"],[\"l1:t0\"]],\"label\":\"できれば\",\"meaningZh\":\"如果可能\",\"grammarZh\":\"表示条件\"}]"}}]}
        """;
        var groups = _parser.Parse(json);
        var group = Assert.Single(groups);
        Assert.Equal("g1", group.ModelGroupId);
        Assert.Equal(2, group.PartTokenIds.Count);
        Assert.Equal(SentenceTokenId.Parse("l1:t0"), group.PartTokenIds[1][0]);
    }

    [Fact]
    public void Parses_empty_group_list()
    {
        var groups = _parser.Parse("""{"choices":[{"message":{"content":"[]"}}]}""");
        Assert.Empty(groups);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.Parse("not json"));
    }

    [Fact]
    public void Extracts_array_wrapped_in_prose_and_code_fence()
    {
        var json = """{"choices":[{"message":{"content":"好的，以下是分组：\n```json\n[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]\n```\n希望对你有帮助。"}}]}""";
        var groups = _parser.Parse(json);
        var group = Assert.Single(groups);
        Assert.Equal("g1", group.ModelGroupId);
    }

    [Fact]
    public void Rejects_group_missing_required_field()
    {
        var json = """{"choices":[{"message":{"content":"[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[],\"label\":\"x\",\"meaningZh\":\"y\"}]"}}]}""";
        Assert.Throws<PhraseResponseException>(() => _parser.Parse(json));
    }

    [Fact]
    public void Rejects_parts_with_non_string_token_id()
    {
        var json = """{"choices":[{"message":{"content":"[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[1]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]"}}]}""";
        Assert.Throws<PhraseResponseException>(() => _parser.Parse(json));
    }

    [Fact]
    public void Rejects_malformed_token_id_string()
    {
        var json = """{"choices":[{"message":{"content":"[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"foo\"]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]"}}]}""";
        Assert.Throws<PhraseResponseException>(() => _parser.Parse(json));
    }
}

public sealed class DeepSeekPhraseAnalyzerTests
{
    [Fact]
    public async Task Returns_no_key_outcome_without_configuration()
    {
        var analyzer = new DeepSeekPhraseAnalyzer(new FakeSettings(), new HttpClient());
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.NoKey, result.Outcome);
    }

    [Fact]
    public async Task Parses_valid_response_into_groups()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"choices":[{"message":{"content":"[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]"}}]}""");
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(handler));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Success, result.Outcome);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task Maps_http_500_to_refused()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "");
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(handler));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task Maps_timeout_to_timeout_outcome()
    {
        var handler = new StubHandler(throwEx: new TaskCanceledException("timeout"));
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(handler));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task Maps_cancellation_to_cancelled_outcome()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"choices":[]}""");
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await analyzer.AnalyzeAsync(Request(), cts.Token);
        Assert.Equal(PhraseAnalysisOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Maps_malformed_response_to_malformed_json()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "not json");
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(handler));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
    }

    [Fact]
    public async Task Sends_bearer_authorization_header()
    {
        string? auth = null;
        var handler = new StubHandler(HttpStatusCode.OK, """{"choices":[]}""", captureAuth: v => auth = v);
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "secret"), new HttpClient(handler));
        await analyzer.AnalyzeAsync(Request());
        Assert.Equal("Bearer secret", auth);
    }

    private static PhraseAnalysisRequest Request()
        => new("s0", "あ", [new SentenceTokenReference(0, 0, 0, 0, Token(), [new ScreenRect(0, 0, 10, 20)])], []);

    private static Token Token()
        => new("あ", "あ", "あ", "あ", "あ", "あ",
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);

    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _apiKey;
        public FakeSettings(string? apiKey = null) => _apiKey = apiKey;
        public string? GetValue(string key) => key == DeepSeekSettingsKeys.ApiKey ? _apiKey : null;
        public void SetValue(string key, string? value) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _throwEx;
        private readonly Action<string>? _captureAuth;

        public StubHandler(HttpStatusCode status, string body, Action<string>? captureAuth = null)
            => (_status, _body, _captureAuth) = (status, body, captureAuth);

        public StubHandler(Exception throwEx)
            => (_body, _throwEx) = ("", throwEx);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwEx is not null)
                throw _throwEx;
            _captureAuth?.Invoke(request.Headers.Authorization?.ToString() ?? "");
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8) });
        }
    }
}