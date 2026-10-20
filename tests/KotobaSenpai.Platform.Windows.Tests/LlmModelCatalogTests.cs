using System.Net;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class LlmModelCatalogTests
{
    [Theory]
    [InlineData(null, LlmProtocolIds.OpenAiChat)]
    [InlineData("unknown", LlmProtocolIds.OpenAiChat)]
    [InlineData("responses", LlmProtocolIds.OpenAiResponses)]
    public void Registry_defaults_and_normalizes_protocol_ids(string? value, string expected)
    {
        var protocol = new LlmProtocolRegistry().Resolve(value);

        Assert.Equal(expected switch
        {
            LlmProtocolIds.OpenAiResponses => "/responses",
            LlmProtocolIds.Anthropic => "/v1/messages",
            _ => "/chat/completions",
        }, protocol.Path);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task Tries_compatibility_fallback_and_uses_anthropic_authentication(HttpStatusCode unavailableStatus)
    {
        var requests = new List<HttpRequestMessage>();
        var catalog = CreateCatalog(request =>
        {
            requests.Add(request);
            return Task.FromResult(requests.Count == 1
                ? Response(unavailableStatus, "")
                : Response(HttpStatusCode.OK, """{"data":[{"id":" model-b ","owned_by":{"unexpected":true}},{"id":"model-a","owned_by":"owner"},{"id":"model-a"},{"id":" "}]}"""));
        });

        var result = await catalog.GetModelsAsync(new("https://provider.example/anthropic", "secret", LlmProtocolIds.Anthropic));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "model-a", "model-b" }, result.Models.Select(model => model.Id));
        Assert.Equal("owner", result.Models[0].Owner);
        Assert.Null(result.Models[1].Owner);
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://provider.example/anthropic/v1/models", requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("https://provider.example/v1/models", requests[1].RequestUri!.AbsoluteUri);
        foreach (var request in requests)
        {
            Assert.Equal("secret", request.Headers.GetValues("x-api-key").Single());
            Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
            Assert.False(request.Headers.Contains("Authorization"));
        }
    }

    [Fact]
    public async Task Uses_versioned_base_and_parses_alternate_catalog_shape()
    {
        Uri? requested = null;
        HttpRequestMessage? capturedRequest = null;
        var catalog = CreateCatalog(request =>
        {
            requested = request.RequestUri;
            capturedRequest = request;
            return Task.FromResult(Response(HttpStatusCode.OK, """{"models":[{"slug":"z-model","id":"ignored"},{"id":"a-model"}]}"""));
        });

        var result = await catalog.GetModelsAsync(new("https://provider.example/api/v4", "secret", LlmProtocolIds.OpenAiChat));

        Assert.True(result.Succeeded);
        Assert.Equal("https://provider.example/api/v4/models", requested!.AbsoluteUri);
        Assert.Equal("Bearer secret", capturedRequest!.Headers.Authorization!.ToString());
        Assert.Equal(new[] { "a-model", "z-model" }, result.Models.Select(model => model.Id));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmModelCatalogFailure.Authentication)]
    [InlineData(HttpStatusCode.NotFound, LlmModelCatalogFailure.NotFound)]
    public async Task Maps_http_failures(HttpStatusCode status, LlmModelCatalogFailure expected)
    {
        var result = await CreateCatalog(_ => Task.FromResult(Response(status, "")))
            .GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat));

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task Continues_when_optional_owner_has_wrong_json_type()
    {
        var result = await CreateCatalog(_ => Task.FromResult(Response(HttpStatusCode.OK, """{"data":[{"id":"model","owned_by":7}]}""")))
            .GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat));

        Assert.True(result.Succeeded);
        Assert.Null(Assert.Single(result.Models).Owner);
    }

    [Theory]
    [InlineData("{}", LlmModelCatalogFailure.Unsupported)]
    [InlineData("{\"data\":[]}", LlmModelCatalogFailure.Empty)]
    [InlineData("not-json", LlmModelCatalogFailure.Unsupported)]
    public async Task Classifies_unusable_success_responses(string body, LlmModelCatalogFailure expected)
    {
        var result = await CreateCatalog(_ => Task.FromResult(Response(HttpStatusCode.OK, body)))
            .GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat));

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task Reports_caller_cancellation()
    {
        using var started = new ManualResetEventSlim();
        var catalog = CreateCatalog(async (_, token) =>
        {
            started.Set();
            await Task.Delay(Timeout.Infinite, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var cancellation = new CancellationTokenSource();
        var pending = catalog.GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat), cancellation.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        cancellation.Cancel();

        var result = await pending;

        Assert.Equal(LlmModelCatalogFailure.Cancelled, result.Failure);
    }

    [Theory]
    [InlineData("", "secret", LlmModelCatalogFailure.MissingEndpoint)]
    [InlineData("https://provider.example", "", LlmModelCatalogFailure.MissingApiKey)]
    public async Task Reports_missing_credentials_without_sending_requests(string endpoint, string key, LlmModelCatalogFailure expected)
    {
        var sent = false;
        var catalog = CreateCatalog(_ =>
        {
            sent = true;
            return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
        });

        var result = await catalog.GetModelsAsync(new(endpoint, key, LlmProtocolIds.OpenAiChat));

        Assert.Equal(expected, result.Failure);
        Assert.False(sent);
    }

    [Fact]
    public async Task Maps_transport_errors_without_returning_exception_details()
    {
        var catalog = CreateCatalog(_ => throw new HttpRequestException("Connection failed for https://provider.example"));

        var result = await catalog.GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat));

        Assert.Equal(LlmModelCatalogFailure.Transport, result.Failure);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Reports_http_client_timeout()
    {
        using var client = new HttpClient(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Response(HttpStatusCode.OK, "{}");
        })) { Timeout = TimeSpan.FromMilliseconds(50) };
        var catalog = new HttpLlmModelCatalog(client, new LlmProtocolRegistry());

        var result = await catalog.GetModelsAsync(new("https://provider.example", "secret", LlmProtocolIds.OpenAiChat));

        Assert.Equal(LlmModelCatalogFailure.Timeout, result.Failure);
    }

    private static HttpLlmModelCatalog CreateCatalog(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        => new(new HttpClient(new DelegateHandler(handler)), new LlmProtocolRegistry());

    private static HttpLlmModelCatalog CreateCatalog(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(new HttpClient(new DelegateHandler(handler)), new LlmProtocolRegistry());

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((request, _) => handler(request)) { }

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
