using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class SentenceTranslationProtocolTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Protocols_include_translation_only_when_requested_and_ignore_unrequested_output(int kind, bool enabled)
    {
        ILlmProtocol protocol = kind switch { 0 => new OpenAiResponsesProtocol(), 1 => new OpenAiChatCompletionsProtocol(), _ => new AnthropicMessagesProtocol() };
        var payload = new { groups = Array.Empty<object>(), words = Array.Empty<object>(), translation = " Reference translation " };
        var serialized = JsonSerializer.Serialize(payload);
        var envelope = kind switch
        {
            0 => JsonSerializer.Serialize(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = serialized } } } } }),
            1 => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = serialized } } } }),
            _ => JsonSerializer.Serialize(new { content = new[] { new { type = "tool_use", name = "return_groups", input = payload } } })
        };
        var handler = new ResponseHandler(envelope);
        var analyzer = new LlmPhraseAnalyzer(new EmptySettings(), new HttpClient(handler), protocol: protocol);
        var request = new PhraseAnalysisRequest(Guid.NewGuid(), "s0", "日本語。", [], [], includeSentenceTranslation: enabled);
        var result = await analyzer.AnalyzeAsync(request, new LlmConfiguration("", "https://example.com/analysis", "test-key", "test-model"), singleEndpoint: true);

        Assert.True(result.Succeeded);
        Assert.Equal(enabled ? "Reference translation" : null, result.Translation);
        Assert.Equal(enabled, handler.Body!.Contains("translation", StringComparison.Ordinal));
        Assert.Equal(1, handler.Calls);
        Assert.Equal(enabled, protocol.BuildBody("system", "user", "model", enabled).Contains("translation", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"translation\":null}")]
    [InlineData("{\"translation\":123}")]
    [InlineData("{\"translation\":[]}")]
    [InlineData("{\"translation\":{}}")]
    [InlineData("{\"translation\":\"  \"}")]
    public void Missing_or_malformed_translation_is_independently_unavailable(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(PhraseResponseParser.ParseTranslation(document.RootElement));
    }

    [Fact]
    public void Translation_length_limit_is_applied_after_trimming_without_truncation()
    {
        var accepted = new string('a', SentenceTranslation.MaximumLength);
        using var valid = JsonDocument.Parse(JsonSerializer.Serialize(new { translation = " " + accepted + " " }));
        using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { translation = accepted + "a" }));
        Assert.Equal(accepted, PhraseResponseParser.ParseTranslation(valid.RootElement));
        Assert.Null(PhraseResponseParser.ParseTranslation(invalid.RootElement));
    }

    [Fact]
    public async Task Connection_checks_honor_the_disabled_translation_preference()
    {
        var content = JsonSerializer.Serialize(new { groups = Array.Empty<object>(), words = new[] {
            new { headword = "日本語", pos = "名詞", meaning = "Japanese", grammar = "Noun" } } });
        var handler = new ResponseHandler(JsonSerializer.Serialize(new { output = new[] {
            new { type = "message", content = new[] { new { type = "output_text", text = content } } } } }));
        var settings = new EmptySettings("false");
        var detector = new LlmConnectionDetector(new LlmPhraseAnalyzer(settings, new HttpClient(handler)),
            new LlmProtocolRegistry(), new SentenceTranslationPreferences(settings));
        var result = await detector.CheckAsync(new LlmConfiguration("openai-responses", "https://example.com/analysis", "test-key", "test-model"));
        Assert.True(result.Succeeded);
        Assert.DoesNotContain("translation", handler.Body!);
    }

    private sealed class EmptySettings(string? translation = null) : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => key == SentenceTranslationPreferences.EnabledKey ? translation : null;
        public override void SetValue(string key, string? value) => throw new InvalidOperationException("Analysis must not save settings.");
    }

    private sealed class ResponseHandler(string envelope) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }
}
