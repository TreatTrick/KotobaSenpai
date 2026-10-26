using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.Net;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Llm;
using LlmSettingsKeys = KotobaSenpai.Core.Llm.LlmSettingsKeys;

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
    public void Body_does_not_add_local_pitch_metadata_to_provider_content()
    {
        var pitch = new PitchAccentSummary("東京", "とうきょう", 0, 2, "[2] LH↓LL");
        var request = new PhraseAnalysisRequest(
            Guid.Parse("0123456789abcdef0123456789abcdef"),
            "s0",
            "東京",
            [Ref()],
            [new LocalSpanSummary("東京", "とうきょう", "東京", [SentenceTokenId.Parse("l0:t0")], [pitch])]);

        var (_, user) = _builder.Build(request);

        Assert.DoesNotContain("pitch", user, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[2] LH", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_uses_english_instructions_independently_of_localization()
    {
        var (system, user) = _builder.Build(Request(Segment("あ")));

        Assert.Contains("You analyze Japanese", system);
        Assert.Contains("Segment text:", user);
        Assert.Contains("token table", user);
        Assert.DoesNotContain("[Llm.", system + user);
    }

    [Fact]
    public void Prompt_contains_explicit_root_object_and_words_instructions()
    {
        var builder = new PhrasePromptBuilder();

        var (system, user) = builder.Build(Request(Segment("あ")), LlmPromptProfile.AnthropicMessages);

        Assert.Contains("Call the `return_groups` tool exactly once", system);
        Assert.Contains("Do not emit a plain-text or Markdown answer", system);
        Assert.Contains("one JSON object with the top-level arrays `groups` and `words`", user);
        Assert.Contains("groups contains only meaningful multi-token combinations", system);
        Assert.Contains("exactly one entry for every local word chunk", user);
    }

    [Fact]
    public void Prompt_profile_uses_protocol_specific_tool_or_json_instructions()
    {
        var builder = new PhrasePromptBuilder();

        var anthropic = builder.Build(Request(Segment("あ")), LlmPromptProfile.AnthropicMessages);
        var chat = builder.Build(Request(Segment("あ")), LlmPromptProfile.OpenAiChatCompletions);
        var responses = builder.Build(Request(Segment("あ")), LlmPromptProfile.OpenAiResponses);

        Assert.Contains("Call the `return_groups` tool exactly once", anthropic.SystemPrompt);
        Assert.DoesNotContain("Call the `return_groups` tool", chat.SystemPrompt);
        Assert.DoesNotContain("Call the `return_groups` tool", responses.SystemPrompt);
        Assert.Contains("JSON object", chat.UserContent);
        Assert.Contains("JSON object", responses.UserContent);
    }

    [Theory]
    [InlineData(LlmPromptProfile.AnthropicMessages, "French")]
    [InlineData(LlmPromptProfile.OpenAiChatCompletions, "French")]
    [InlineData(LlmPromptProfile.OpenAiResponses, "French")]
    [InlineData(LlmPromptProfile.AnthropicMessages, "English")]
    [InlineData(LlmPromptProfile.OpenAiChatCompletions, "Simplified Chinese")]
    [InlineData(LlmPromptProfile.OpenAiResponses, "日本語")]
    [InlineData(LlmPromptProfile.OpenAiChatCompletions, "French \"formal\"\\variant")]
    public void Target_language_is_quoted_data_and_applies_to_all_explanations(LlmPromptProfile profile, string language)
    {
        var request = new PhraseAnalysisRequest(Guid.NewGuid(), "s0", "あ", [Ref()], [], language);
        var (system, user) = _builder.Build(request, profile);
        var encodedName = system[(system.LastIndexOf("Target explanation language: ", StringComparison.Ordinal)
            + "Target explanation language: ".Length)..];

        Assert.Equal(language, JsonSerializer.Deserialize<string>(encodedName));
        Assert.Contains("ALL `meaning` and `grammar` values in BOTH `groups` and `words`", system);
        Assert.Contains("interpret the language it names", system);
        Assert.Contains("Do not add bilingual explanations", system);
        Assert.Contains("Necessary Japanese quotations", system);
        Assert.Contains("Structure examples illustrate shape only", system);
        Assert.DoesNotContain("best contextual Chinese meaning", user);
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
        => new(Guid.Parse("0123456789abcdef0123456789abcdef"), "s0", text, [Ref()], [new LocalSpanSummary(text, text, text, [SentenceTokenId.Parse("l0:t0")])]);

    private static string Segment(string text) => text;
}

public sealed class PhraseResponseParserTests
{
    private readonly PhraseResponseParser _parser = new();

    [Fact]
    public void Parses_multi_part_and_cross_line_groups()
    {
        var groups = _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0"],["l1:t0"]],"label":"できれば","meaning":"如果可能","grammar":"表示条件"}]"""));
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
            """[{"modelGroupId":"g1","type":"grammar","parts":[],"label":"x","meaning":"y"}]""")));
    }

    [Fact]
    public void Skips_parts_with_non_string_token_id()
    {
        var groups = _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[[1]],"label":"x","meaning":"y","grammar":"z"}]"""));
        Assert.Empty(Assert.Single(groups).PartTokenIds[0]);
    }

    [Fact]
    public void Skips_malformed_token_id_string()
    {
        var groups = _parser.ParseGroups(Groups(
            """[{"modelGroupId":"g1","type":"grammar","parts":[["foo"]],"label":"x","meaning":"y","grammar":"z"}]"""));
        Assert.Empty(Assert.Single(groups).PartTokenIds[0]);
    }

    [Fact]
    public void Parses_valid_words()
    {
        var words = _parser.ParseWords(Words(
            """[{"headword":"来","pos":"自動・カ変","meaning":"来","grammar":"カ変活用"}]"""));
        var word = Assert.Single(words);
        Assert.Equal("来", word.Headword);
        Assert.Equal("自動・カ変", word.Pos);
        Assert.Equal("来", word.Meaning);
    }

    [Fact]
    public void Parses_empty_word_list()
    {
        Assert.Empty(_parser.ParseWords(Words("[]")));
    }

    [Fact]
    public void Rejects_non_array_words_root()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseWords(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Rejects_word_missing_required_field()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseWords(Words(
            """[{"headword":"学校","pos":"名詞","meaning":"学校"}]""")));
    }

    [Fact]
    public void Rejects_word_missing_headword()
    {
        Assert.Throws<PhraseResponseException>(() => _parser.ParseWords(Words(
            """[{"pos":"名詞","meaning":"学校","grammar":"名詞"}]""")));
    }

    private static JsonElement Groups(string arrayJson)
        => JsonDocument.Parse(arrayJson).RootElement.Clone();

    private static JsonElement Words(string arrayJson)
        => JsonDocument.Parse(arrayJson).RootElement.Clone();
}

public sealed class LlmProtocolTests
{
    private const string Group =
        """{"groups":[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0"]],"label":"x","meaning":"y","grammar":"z"}]}""";

    [Fact]
    public void OpenAiChatCompletions_builds_json_object_envelope_with_an_output_example()
    {
        var protocol = new OpenAiChatCompletionsProtocol();
        Assert.Equal("/chat/completions", protocol.Path);

        var body = protocol.BuildBody("sys", "user", "m");
        Assert.Contains("response_format", body);
        Assert.DoesNotContain("json_schema", body);
        Assert.DoesNotContain("\"strict\"", body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(4096, root.GetProperty("max_tokens").GetInt32());
        var system = root.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.StartsWith("sys", system);
        Assert.Contains("JSON", system);
        using var example = JsonDocument.Parse(system[system.IndexOf('{')..]);
        Assert.Single(new PhraseResponseParser().ParseGroups(example.RootElement.GetProperty("groups")));
        Assert.Single(new PhraseResponseParser().ParseWords(example.RootElement.GetProperty("words")));
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public void OpenAiChatCompletions_disables_reasoning_for_supported_providers()
    {
        var body = new OpenAiChatCompletionsProtocol().BuildBody("sys", "user", "m");
        using var document = JsonDocument.Parse(body);

        Assert.Equal("low", document.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
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
    public void OpenAiChatCompletions_extracts_words_and_defaults_to_empty_when_absent()
    {
        var protocol = new OpenAiChatCompletionsProtocol();
        var withWords = """{"groups":[],"words":[{"headword":"学校","pos":"名詞","meaning":"学校","grammar":"名詞"}]}""";
        var envelope = "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(withWords) + "}}]}";
        Assert.Equal(1, GetArrayLength(protocol.ExtractWordsJson(envelope)));

        var withoutWords = "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(Group) + "}}]}";
        Assert.Equal(0, GetArrayLength(protocol.ExtractWordsJson(withoutWords)));
    }

    [Fact]
    public void AnthropicMessages_builds_forced_tool_use_envelope()
    {
        var protocol = new AnthropicMessagesProtocol();
        var body = protocol.BuildBody("sys", "user", "m");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Contains("\"thinking\"", body);
        Assert.Contains("\"disabled\"", body);
        Assert.True(root.TryGetProperty("tools", out var tools));
        var tool = tools[0];
        Assert.Equal("return_groups", tool.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Object, tool.GetProperty("input_schema").ValueKind);
        Assert.True(root.TryGetProperty("tool_choice", out var toolChoice));
        Assert.Equal("tool", toolChoice.GetProperty("type").GetString());
        Assert.Equal("return_groups", toolChoice.GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("output_config", out _));
    }

    [Fact]
    public void Protocols_apply_their_own_authentication_headers()
    {
        foreach (var protocol in new ILlmProtocol[]
        {
            new OpenAiChatCompletionsProtocol(),
            new OpenAiResponsesProtocol(),
        })
        {
            using var request = new HttpRequestMessage();
            protocol.ApplyAuthentication(request, "secret");
            Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("x-api-key"));
        }

        using var anthropicRequest = new HttpRequestMessage();
        new AnthropicMessagesProtocol().ApplyAuthentication(anthropicRequest, "secret");
        Assert.Equal("secret", anthropicRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", anthropicRequest.Headers.GetValues("anthropic-version").Single());
        Assert.False(anthropicRequest.Headers.Contains("Authorization"));
    }

    [Fact]
    public void AnthropicMessages_extracts_groups_from_tool_use_content()
    {
        var protocol = new AnthropicMessagesProtocol();
        var envelope = """{"content":[{"type":"tool_use","name":"return_groups","input":{"groups":[{"modelGroupId":"g1","type":"grammar","parts":[["l0:t0"]],"label":"x","meaning":"y","grammar":"z"}],"words":[{"headword":"学校","pos":"名詞","meaning":"学校","grammar":"名詞"}]}}]}""";
        var groups = protocol.ExtractGroupsJson(envelope);
        Assert.Equal(1, GetArrayLength(groups));
        Assert.Equal(1, GetArrayLength(protocol.ExtractWordsJson(envelope)));
    }

    [Fact]
    public void OpenAiResponses_builds_text_format_envelope_and_extracts_from_output_text()
    {
        var protocol = new OpenAiResponsesProtocol();
        var body = protocol.BuildBody("sys", "user", "m");
        Assert.Contains("\"text\"", body);
        Assert.Contains("\"format\"", body);

        using var request = JsonDocument.Parse(body);
        Assert.Equal(4096, request.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("none", request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());

        var envelope = "{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":"
            + JsonSerializer.Serialize(Group) + "}]}]}";
        var groups = protocol.ExtractGroupsJson(envelope);
        Assert.Equal(1, GetArrayLength(groups));
    }

    [Fact]
    public void AnthropicMessages_throws_when_no_tool_use_block()
    {
        var protocol = new AnthropicMessagesProtocol();
        Assert.Throws<PhraseResponseException>(() => protocol.ExtractGroupsJson("""{"content":[{"type":"text","text":"{}"}]}"""));
    }

    private static int GetArrayLength(JsonElement array)
    {
        var count = 0;
        foreach (var _ in array.EnumerateArray())
            count++;
        return count;
    }
}

public sealed class LlmPhraseAnalyzerTests
{
    private const string GroupEnvelope =
        """{"choices":[{"message":{"content":"{\"groups\":[{\"modelGroupId\":\"g1\",\"type\":\"grammar\",\"parts\":[[\"l0:t0\"]],\"label\":\"x\",\"meaning\":\"y\",\"grammar\":\"z\"}]}"}}]}""";

    [Fact]
    public async Task Returns_no_key_outcome_without_configuration()
    {
        var analyzer = new LlmPhraseAnalyzer(new FakeSettings(), new HttpClient());
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.NoKey, result.Outcome);
    }

    [Fact]
    public async Task Disabled_cloud_analysis_does_not_send_a_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK, GroupEnvelope);
        var settings = new FakeSettings(apiKey: "secret");
        settings.SetValue(LlmSettingsKeys.Enabled, "false");
        var analyzer = new LlmPhraseAnalyzer(settings, new HttpClient(handler));

        var result = await analyzer.AnalyzeAsync(Request());

        Assert.Equal(PhraseAnalysisOutcome.NoKey, result.Outcome);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task Parses_valid_response_into_groups()
    {
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, GroupEnvelope)));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Success, result.Outcome);
        Assert.Single(result.Groups);
    }

    [Fact]
    public async Task Records_raw_request_and_response_exchange()
    {
        var reporter = new FakeDiagnosticReporter();
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "secret-api-key"), new HttpClient(new StubHandler(HttpStatusCode.OK, GroupEnvelope)),
            diagnostics: reporter);
        await analyzer.AnalyzeAsync(Request());

        Assert.NotNull(reporter.RequestJson);
        Assert.NotNull(reporter.ResponseJson);
        Assert.Equal(Request().RecognitionId, reporter.RecognitionId);
        Assert.Equal("s0", reporter.SegmentId);
        Assert.Contains("test-model", reporter.RequestJson); // model name in the request body
        Assert.DoesNotContain(Request().RecognitionId.ToString("N"), reporter.RequestJson);
        Assert.DoesNotContain("secret-api-key", reporter.RequestJson);
        Assert.Contains("groups", reporter.ResponseJson);        // raw provider envelope saved verbatim
        Assert.Contains("modelGroupId", reporter.GroupsJson);
        Assert.Equal("[]", reporter.WordsJson);
    }

    [Fact]
    public async Task Parses_words_into_result()
    {
        const string envelope = """{"choices":[{"message":{"content":"{\"groups\":[],\"words\":[{\"headword\":\"来\",\"pos\":\"自動・カ変\",\"meaning\":\"来\",\"grammar\":\"カ変活用\"}]}"}}]}""";
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, envelope)));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Success, result.Outcome);
        var word = Assert.Single(result.Words);
        Assert.Equal("来", word.Headword);
        Assert.Equal("自動・カ変", word.Pos);
    }

    [Fact]
    public async Task Maps_http_500_to_refused()
    {
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task Maps_timeout_to_timeout_outcome()
    {
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(throwEx: new TaskCanceledException("timeout"))));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task Maps_cancellation_to_cancelled_outcome()
    {
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[]}""")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await analyzer.AnalyzeAsync(Request(), cts.Token);
        Assert.Equal(PhraseAnalysisOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Maps_malformed_response_to_malformed_json()
    {
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"), new HttpClient(new StubHandler(HttpStatusCode.OK, "not json")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
    }

    [Fact]
    public async Task Maps_wrong_shape_envelope_to_malformed_json_not_crash()
    {
        // Valid JSON but missing the message field → old code let KeyNotFoundException escape; should map to MalformedJson.
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "k"),
            new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[{}]}""")));
        var result = await analyzer.AnalyzeAsync(Request());
        Assert.Equal(PhraseAnalysisOutcome.MalformedJson, result.Outcome);
    }

    [Fact]
    public async Task Sends_bearer_authorization_header()
    {
        string? auth = null;
        var analyzer = new LlmPhraseAnalyzer(
            new FakeSettings(apiKey: "secret"),
            new HttpClient(new StubHandler(HttpStatusCode.OK, """{"choices":[]}""", captureAuth: v => auth = v)));
        await analyzer.AnalyzeAsync(Request());
        Assert.Equal("Bearer secret", auth);
    }

    [Fact]
    public async Task Resolves_protocol_and_authentication_from_current_settings_for_each_request()
    {
        var settings = new FakeSettings(apiKey: "secret");
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://example.com/chat/completions");
        var requests = new List<(string Path, string? Authorization, string? ApiKey)>();
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "", captureRequest: request =>
            requests.Add((request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null)));
        var analyzer = new LlmPhraseAnalyzer(settings, new HttpClient(handler));

        await analyzer.AnalyzeAsync(Request());
        settings.SetValue(LlmSettingsKeys.Protocol, LlmProtocolIds.Anthropic);
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://example.com/v1/messages");
        await analyzer.AnalyzeAsync(Request());
        settings.SetValue(LlmSettingsKeys.Protocol, LlmProtocolIds.OpenAiResponses);
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://example.com/v1/responses");
        await analyzer.AnalyzeAsync(Request());

        Assert.Equal("/chat/completions", requests[0].Path);
        Assert.Equal("Bearer secret", requests[0].Authorization);
        Assert.Equal("/v1/messages", requests[1].Path);
        Assert.Null(requests[1].Authorization);
        Assert.Equal("secret", requests[1].ApiKey);
        Assert.Equal("/v1/responses", requests[2].Path);
        Assert.Equal("Bearer secret", requests[2].Authorization);
    }

    private static PhraseAnalysisRequest Request()
        => new(Guid.Parse("0123456789abcdef0123456789abcdef"), "s0", "あ", [new SentenceTokenReference(0, 0, 0, 0, Token(), [new ScreenRect(0, 0, 10, 20)])], []);

    private static Token Token()
        => new("あ", "あ", "あ", "あ", "あ", "あ",
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);

    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = new()
        {
            [LlmSettingsKeys.ApiKey] = null,
            [LlmSettingsKeys.Endpoint] = "https://example.com",
            [LlmSettingsKeys.Model] = "test-model",
            [LlmSettingsKeys.Protocol] = LlmProtocolIds.OpenAiChat,
            [LlmSettingsKeys.Enabled] = "true",
        };

        public FakeSettings(string? apiKey = null) => _values[LlmSettingsKeys.ApiKey] = apiKey;
        public string? GetValue(string key) => _values.GetValueOrDefault(key);
        public void SetValue(string key, string? value) => _values[key] = value;
    }

    private sealed class FakeDiagnosticReporter : IDiagnosticReporter
    {
        public Guid RecognitionId { get; private set; }
        public string? SegmentId { get; private set; }
        public string? RequestJson { get; private set; }
        public string? ResponseJson { get; private set; }
        public string GroupsJson { get; private set; } = string.Empty;
        public string WordsJson { get; private set; } = string.Empty;
        public void RecordTokens(Guid recognitionId, WindowTarget target, IReadOnlyList<GroupedWord> groupedWords) { }
        public void RecordPhraseAnalysis(Guid recognitionId, PhraseAnalysisOutcome outcome, IReadOnlyList<PhraseGroupView> groups, string? warning) { }
        public void RecordLlmExchange(Guid recognitionId, string segmentId, string requestJson, string responseJson, string groupsJson, string wordsJson)
        {
            RecognitionId = recognitionId;
            SegmentId = segmentId;
            RequestJson = requestJson;
            ResponseJson = responseJson;
            GroupsJson = groupsJson;
            WordsJson = wordsJson;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _throwEx;
        private readonly Action<string>? _captureAuth;
        private readonly Action<HttpRequestMessage>? _captureRequest;
        public bool WasCalled { get; private set; }

        public StubHandler(HttpStatusCode status, string body, Action<string>? captureAuth = null, Action<HttpRequestMessage>? captureRequest = null)
            => (_status, _body, _captureAuth, _captureRequest) = (status, body, captureAuth, captureRequest);

        public StubHandler(Exception throwEx)
            => (_body, _throwEx) = ("", throwEx);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            if (_throwEx is not null)
                throw _throwEx;
            _captureAuth?.Invoke(request.Headers.Authorization?.ToString() ?? "");
            _captureRequest?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8) });
        }
    }
}
