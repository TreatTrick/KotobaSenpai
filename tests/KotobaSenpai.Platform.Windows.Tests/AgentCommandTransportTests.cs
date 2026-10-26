using System.Net;
using System.Net.Http;
using System.IO;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class AgentCommandTransportTests
{
    [Fact]
    public async Task Catalog_accepts_a_complete_analysis_endpoint_and_preserves_prefix_and_query()
    {
        Uri? received = null;
        var http = new HttpClient(new CallbackHandler(request =>
        {
            received = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"selected-model"}]}""") };
        }));
        var catalog = new HttpLlmModelCatalog(http, new());
        var result = await catalog.GetModelsAsync(new("https://provider.example/custom/v1/responses?tenant=demo", "test-key", LlmProtocolIds.OpenAiResponses));

        Assert.True(result.Succeeded);
        Assert.Equal("/custom/v1/models", received!.AbsolutePath);
        Assert.Equal("?tenant=demo", received.Query);
    }

    [Fact]
    public async Task Malformed_input_returns_a_safe_error_without_echoing_the_payload()
    {
        var result = await AgentCommandProtocol.ReadAsync(new StringReader("""{"schemaVersion":1,"apiKey":"secret","model":123}"""), "configure", default);
        Assert.Equal("invalid_json", result.Error!.Code);
        Assert.DoesNotContain("secret", AgentCommandProtocol.Serialize(result.Error));
    }

    [Fact]
    public void Redaction_preserves_json_structure_and_handles_escaped_credentials()
    {
        const string secret = "key\"with\\escapes";
        var result = new AgentApiResult { Models = [new("provider-" + secret)] };
        var json = AgentCommandProtocol.Serialize(result, secret);
        Assert.DoesNotContain("escapes", json);
        Assert.Contains("schemaVersion", json);
        Assert.Contains("REDACTED", json);
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
