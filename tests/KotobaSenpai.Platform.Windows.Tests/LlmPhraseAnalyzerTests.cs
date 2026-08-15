using System.Net;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePromptBuilderTests
{
    private readonly PhrasePromptBuilder _builder = new();

    [Fact]
    public void Body_contains_token_ids_and_metadata_but_no_key_or_offsets()
    {
        var (system, user) = _builder.Build(Request(Segment("あ")));

        Assert.Contains("l0:t0", user);
        Assert.DoesNotContain("apiKey", system + user);
        Assert.DoesNotContain("screenshot", user);
        Assert.DoesNotContain("offset", user);
    }

    [Fact]
    public void Body_rejects_oversized_segment_text()
    {
        var large = new string('あ', 20_000);
        Assert.Throws<RequestTooLargeException>(() => _builder.Build(Request(Segment(large))));
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
        var groups = _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0"],["l1:t0"]],"label":"できれば","meaningZh":"如果可能","grammarZh":"表示条件"}]"""));
        var group = Assert.Single(groups);
        Assert.Equal("g1", group.ModelGroupId);
        Assert.Equal(2, group.PartTokenIds.Count);
        Assert.Equal(SentenceTokenId.Parse("l1:t0"), group.PartTokenIds[1][0]);
    }

    [Fact]
    public void Parses_empty_group_list()
    {
        var groups = _parser.ParseGroups(Groups("[]"));
        Assert.Empty(groups);
    }

    [Fact]
    public void Rejects_non_array_root()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseGroups(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Rejects_group_missing_required_field()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[],"label":"x","meaningZh":"y"}]""")));
    }

    [Fact]
    public void Rejects_parts_with_non_string_token_id()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[[1]],"label":"x","meaningZh":"y","grammarZh":"z"}]""")));
    }

    [Fact]
    public void Rejects_malformed_token_id_string()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[["foo"]],"label":"x","meaningZh":"y","grammarZh":"z"}]""")));
    }

    private static JsonElement Groups(string arrayJson)
        => JsonDocument.Parse(arrayJson).RootElement.Clone();
}

public sealed class LlmProtocolTests
{
    private const string Group =
        """{"groups":[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0"]],"label":"x","meaningZh":"y","grammarZh":"z"}]}""";

    [Fact]
    public void OpenAiChatCompletions_builds_strict_schema_envelope()
    {
        var protocol = new OpenAiChatCompletionsProtocol();
        Assert.Equal("/chat/completions", protocol.Path);

        var body = protocol.BuildBody("sys", "user", "m");
        Assert.Contains("response_format", body);
        Assert.Contains("json_schema", body);
        Assert.Contains("strict", body);
    }

    [Fact]
    public void OpenAiChatCompletions_extracts_from_choices_content()
    {
        var protocol = new OpenAiChatCompletionsProtocol();
        var envelope = "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(Group) + "}}]}";
        var groups = protocol.ExtractGroupsJson(envelope);
        Assert.Equal(JsonValueKind.Array, groups.ValueKind);
        Assert.Equal(1, GetArrayLength(groups));
    }

    [Fact]
    public void AnthropicMessages_builds_fast_text_envelope()
    {
        var protocol = new AnthropicMessagesProtocol();
        var body = protocol.BuildBody("sys", "user", "m");
        Assert.Contains("\"thinking\"", body);
        Assert.Contains("\"disabled\"", body);
        Assert.DoesNotContain("\"tools\"", body);
        Assert.DoesNotContain("\"tool_choice\"", body);
    }

    [Fact]
    public void AnthropicMessages_extracts_groups_from_text_block()
    {
        var protocol = new AnthropicMessagesProtocol();
        var envelope = """{"content":[{"type":"text","text":"\n```json\n{\"groups\":[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]}\n```\n"}]}""";
        var groups = protocol.ExtractGroupsJson(envelope);
        Assert.Equal(1, GetArrayLength(groups));
    }

    [Fact]
    public void OpenAiResponses_builds_text_format_envelope_and_extracts_from_output_text()
    {
        var protocol = new OpenAiResponsesProtocol();
        var body = protocol.BuildBody("sys", "user", "m");
        Assert.Contains("\"text\"", body);
        Assert.Contains("\"format\"", body);

        var envelope = "{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":"
            + JsonSerializer.Serialize(Group) + "}]}]}";
        var groups = protocol.ExtractGroupsJson(envelope);
        Assert.Equal(1, GetArrayLength(groups));
    }

    [Fact]
    public void AnthropicMessages_throws_when_no_text_block()
    {
        var protocol = new AnthropicMessagesProtocol();
        Assert.Throws<PhraseResponseException>(() => protocol.ExtractGroupsJson("""{"content":[]}"""));
    }

    private static int GetArrayLength(JsonElement array)
    {
        var count = 0;
        foreach (var _ in array.EnumerateArray())
            count++;
        return count;
    }
}

public sealed class DeepSeekPhraseAnalyzerTests
{
    private const string GroupEnvelope =
        """{"choices":[{"message":{"content":"{\"groups\":[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"]],\"label\":\"x\",\"meaningZh\":\"y\",\"grammarZh\":\"z\"}]}"}}]}""";

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
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, GroupEnvelope)));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Success, result.Outcome);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task Maps_http_500_to_refused()
    {
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task Maps_timeout_to_timeout_outcome()
    {
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(throwEx: new TaskCanceledException("timeout"))));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task Maps_cancellation_to_cancelled_outcome()
    {
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[]}""")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await analyzer.AnalyzeAsync(Request(), cts.Token);
        Assert.Equal(PhraseAnalysisOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Maps_malformed_response_to_malformed_json()
    {
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, "not json")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
    }

    [Fact]
    public async Task Maps_wrong_shape_envelope_to_malformed_json_not_crash()
    {
        // 合法 JSON 但缺 message 字段 → 旧代码抛 KeyNotFoundException 逃逸；应映射为 MalformedJson。
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "k"),
            new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[{}]}""")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
    }

    [Fact]
    public async Task Sends_bearer_authorization_header()
    {
        string? auth = null;
        var analyzer = new DeepSeekPhraseAnalyzer(
            new FakeSettings(apiKey: "secret"),
            new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[]}""", captureAuth: v => auth = v)));
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
        public string? GetValue(string key) => key switch
        {
            DeepSeekSettingsKeys.ApiKey => _apiKey,
            DeepSeekSettingsKeys.Endpoint => "https://example.com",
            DeepSeekSettingsKeys.Model => "deepseek-chat",
            _ => null,
        };
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