using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Platform.Windows.Settings;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Platform.Windows.Llm;
using LlmSettingsKeys = KotobaSenpai.Core.Llm.LlmSettingsKeys;

namespace KotobaSenpai.App.Tests;

public sealed class AgentApiConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ks-agent-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");
    private static AgentApiRequest Request => new()
        { SchemaVersion = 1, Endpoint = "https://provider.example/custom/v1?tenant=demo", ApiKey = "test-secret", Model = "selected-model" };

    [Theory]
    [InlineData(LlmProtocolIds.OpenAiResponses)]
    [InlineData(LlmProtocolIds.Anthropic)]
    [InlineData(LlmProtocolIds.OpenAiChat)]
    public async Task Checking_current_configuration_uses_only_the_selected_protocol_and_never_applies_connection_fields(string protocol)
    {
        var settings = new SettingsService(FilePath);
        settings.TryUpdate(new Dictionary<string, string?>(), new LlmConfiguration(protocol,
            Request.Endpoint!, Request.ApiKey!, Request.Model!).ToSnapshot());
        var before = settings.GetSnapshot(LlmConfiguration.Keys);
        var handler = new ProviderHandler(protocol);
        var service = Create(settings, handler);

        var result = await service.CheckCurrentAsync();

        Assert.Equal("ok", result.Code);
        Assert.False(result.Applied);
        Assert.True(result.Configuration!.Verified);
        Assert.Equal(protocol, Assert.Single(handler.Calls).Protocol);
        Assert.Equal(before.OrderBy(entry => entry.Key), settings.GetSnapshot(LlmConfiguration.Keys).OrderBy(entry => entry.Key));

        var differentProtocol = protocol == LlmProtocolIds.OpenAiResponses ? LlmProtocolIds.OpenAiChat : LlmProtocolIds.OpenAiResponses;
        var incompatible = new ProviderHandler(differentProtocol);
        Assert.Equal("authentication_failed", (await Create(settings, incompatible).CheckCurrentAsync()).Code);
        Assert.Equal(protocol, Assert.Single(incompatible.Calls).Protocol);
        Assert.False(service.GetStatus().Verified);
        Assert.Equal(before.OrderBy(entry => entry.Key), settings.GetSnapshot(LlmConfiguration.Keys).OrderBy(entry => entry.Key));
    }

    [Fact]
    public async Task Checking_current_configuration_rejects_a_result_for_concurrently_edited_fields()
    {
        var settings = new SettingsService(FilePath);
        settings.TryUpdate(new Dictionary<string, string?>(), new LlmConfiguration(LlmProtocolIds.OpenAiResponses,
            Request.Endpoint!, Request.ApiKey!, Request.Model!).ToSnapshot());
        var service = Create(settings, new ProviderHandler(onRequest: () => settings.SetValue(LlmSettingsKeys.Model, "edited-model")));
        var result = await service.CheckCurrentAsync();
        Assert.Equal("conflict", result.Code);
        Assert.False(result.Applied);
        Assert.False(service.GetStatus().Verified);
        Assert.Equal("edited-model", settings.GetValue(LlmSettingsKeys.Model));
    }

    [Fact]
    public async Task Verification_status_survives_restart_and_is_invalidated_by_configuration_changes()
    {
        var settings = new SettingsService(FilePath);
        var configuration = new LlmConfiguration(LlmProtocolIds.OpenAiResponses,
            Request.Endpoint!, Request.ApiKey!, Request.Model!);
        settings.TryUpdate(new Dictionary<string, string?>(), configuration.ToSnapshot());
        var service = Create(settings, new ProviderHandler());
        var unverified = service.GetStatus();

        Assert.True((await service.ExecuteAsync("configure", Request)).Applied);
        var verified = service.GetStatus();
        Assert.NotEqual(unverified, verified);
        Assert.DoesNotContain(Request.ApiKey!, settings.GetValue("LlmVerifiedConfiguration")!);
        Assert.Equal(verified, Create(new SettingsService(FilePath), new ProviderHandler()).GetStatus());

        foreach (var entry in configuration.ToSnapshot())
        {
            settings.SetValue(entry.Key, entry.Value + "-edited");
            Assert.NotEqual(verified, service.GetStatus());
            settings.SetValue(entry.Key, entry.Value);
            Assert.Equal(verified, service.GetStatus());
        }

        var unavailable = Create(settings, new ProviderHandler(status: HttpStatusCode.ServiceUnavailable));
        await unavailable.ExecuteAsync("configure", Request with { Model = "different-candidate" });
        Assert.Equal(verified, service.GetStatus());
        await unavailable.ExecuteAsync("detect", Request);
        Assert.Equal(verified, service.GetStatus());

        var failure = await Create(settings, new ProviderHandler(status: HttpStatusCode.ServiceUnavailable))
            .ExecuteAsync("configure", Request with { Endpoint = configuration.Endpoint });
        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Equal(unverified, service.GetStatus());
    }

    [Fact]
    public async Task Removed_undo_command_does_not_call_the_provider_or_modify_configuration()
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue(LlmSettingsKeys.Model, "current-model");
        var before = File.ReadAllText(FilePath);
        var handler = new ProviderHandler();

        var result = await Create(settings, handler).ExecuteAsync("undo", new() { SchemaVersion = 1 });

        Assert.Equal("unknown_command", result.Code);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(handler.Calls);
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("YOUR_API_KEY")]
    public async Task Missing_key_does_not_call_the_provider_or_write_settings(string? key)
    {
        var settings = new SettingsService(FilePath);
        var handler = new ProviderHandler();
        var result = await Create(settings, handler).ExecuteAsync("configure", Request with { ApiKey = key });
        Assert.Equal("missing_api_key", result.Code);
        Assert.Empty(handler.Calls);
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(LlmProtocolIds.OpenAiResponses)]
    [InlineData(LlmProtocolIds.Anthropic)]
    [InlineData(LlmProtocolIds.OpenAiChat)]
    public async Task Detects_protocol_in_priority_order_and_applies_a_verified_complete_configuration(string supported)
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue("Theme", "Dark");
        var handler = new ProviderHandler(supported);
        var result = await Create(settings, handler).ExecuteAsync("configure", Request);

        Assert.True(result.Applied);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(supported, result.Configuration!.Protocol);
        Assert.Null(settings.GetValue("LlmConfigurationUndo"));
        Assert.Equal(LlmProtocolIds.OpenAiResponses, handler.Calls[0].Protocol);
        Assert.All(handler.Calls, call => Assert.Contains("tenant=demo", call.Uri.Query));
        Assert.Equal("test-secret", new SettingsService(FilePath).GetValue(LlmSettingsKeys.ApiKey));
        Assert.Equal("Dark", settings.GetValue("Theme"));
        Assert.DoesNotContain(Request.ApiKey!, JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(LlmProtocolIds.OpenAiResponses)]
    [InlineData(LlmProtocolIds.Anthropic)]
    [InlineData(LlmProtocolIds.OpenAiChat)]
    public async Task Detect_is_read_only_and_explicit_requests_reuse_the_normal_analyzer(string protocol)
    {
        var settings = new SettingsService(FilePath);
        var handler = new ProviderHandler(protocol);
        var result = await Create(settings, handler).ExecuteAsync("detect", Request);
        Assert.Equal("ok", result.Code);
        Assert.True(result.Configuration!.Verified);
        Assert.False(result.Applied);
        Assert.False(File.Exists(FilePath));

        var configuration = new LlmConfiguration(protocol, Request.Endpoint!, Request.ApiKey!, Request.Model!);
        settings.TryUpdate(new Dictionary<string, string?>(), configuration.ToSnapshot());
        var analyzer = new LlmPhraseAnalyzer(settings, new HttpClient(handler));
        var sample = LlmConnectionDetector.CreateSample();
        await analyzer.AnalyzeAsync(sample);
        await analyzer.AnalyzeAsync(sample, configuration);
        Assert.Equal(handler.Calls[^1].Body, handler.Calls[^2].Body);
        Assert.Equal(handler.Calls[^1].Uri, handler.Calls[^2].Uri);
    }

    [Fact]
    public async Task Empty_analysis_is_not_success_and_does_not_replace_the_existing_configuration()
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue(LlmSettingsKeys.Model, "old-model");
        var before = File.ReadAllText(FilePath);
        var result = await Create(settings, new ProviderHandler(empty: true, allProtocols: true)).ExecuteAsync("configure", Request);
        Assert.Equal("invalid_analysis", result.Code);
        Assert.False(result.Applied);
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task Legacy_undo_metadata_cannot_conflict_with_applying_a_new_configuration()
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue("LlmConfigurationUndo", "obsolete-metadata");
        settings.SetValue("PhraseGroupsEnabled", "false");
        var handler = new ProviderHandler(onRequest: () => settings.SetValue("LlmConfigurationUndo", "changed-legacy-metadata"));
        var service = Create(settings, handler);

        Assert.True((await service.ExecuteAsync("configure", Request)).Applied);
        Assert.Equal("changed-legacy-metadata", settings.GetValue("LlmConfigurationUndo"));
        Assert.Equal(Request.Model, new SettingsService(FilePath).GetValue(LlmSettingsKeys.Model));
        Assert.Equal("false", settings.GetValue("PhraseGroupsEnabled"));
    }

    [Fact]
    public async Task Model_discovery_tries_distinct_authentication_and_does_not_rank_or_probe_models()
    {
        var calls = new List<HttpRequestMessage>();
        var http = new HttpClient(new CallbackHandler(message =>
        {
            calls.Add(message);
            return message.Headers.Contains("x-api-key")
                ? new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[{"id":"second-model"},{"id":"first-model"}]}""") }
                : new(HttpStatusCode.Unauthorized) { Content = new StringContent("not accepted") };
        }));
        var settings = new SettingsService(FilePath);
        var registry = new LlmProtocolRegistry();
        var service = new LlmConfigurationService(settings, new LlmConnectionDetector(new(settings, http), registry), new HttpLlmModelCatalog(http, registry));
        var result = await service.ExecuteAsync("models", Request with { Model = null });
        Assert.Equal("ok", result.Code);
        Assert.Equal(2, result.Models.Count);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, message => Assert.Equal(HttpMethod.Get, message.Method));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task Failed_atomic_application_does_not_publish_candidate_values()
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue(LlmSettingsKeys.Model, "old");
        var before = File.ReadAllText(FilePath);
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => Create(settings, new ProviderHandler()).ExecuteAsync("configure", Request));
            Assert.Equal("old", settings.GetValue(LlmSettingsKeys.Model));
            Assert.Null(settings.GetValue("LlmConfigurationUndo"));
        }
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task Cancellation_before_commit_preserves_existing_configuration()
    {
        var settings = new SettingsService(FilePath);
        settings.SetValue(LlmSettingsKeys.Model, "before");
        using var cancellation = new CancellationTokenSource();
        var service = Create(settings, new ProviderHandler(onRequest: cancellation.Cancel));
        var result = await service.ExecuteAsync("configure", Request, cancellation.Token);
        Assert.Equal(130, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Equal("before", settings.GetValue(LlmSettingsKeys.Model));
    }

    [Fact]
    public async Task Concurrent_related_edit_rejects_application()
    {
        var settings = new SettingsService(FilePath);
        var service = Create(settings, new ProviderHandler(onRequest: () => settings.SetValue(LlmSettingsKeys.Model, "manual")));
        Assert.Equal("conflict", (await service.ExecuteAsync("configure", Request)).Code);
        Assert.Equal("manual", settings.GetValue(LlmSettingsKeys.Model));
    }

    [Theory]
    [InlineData(429, "rate_limited")]
    [InlineData(503, "provider_unavailable")]
    [InlineData(302, "redirect_rejected")]
    public async Task Temporary_errors_stop_without_trying_more_protocols(int status, string code)
    {
        var handler = new ProviderHandler(status: (HttpStatusCode)status);
        var result = await Create(new SettingsService(FilePath), handler).ExecuteAsync("configure", Request);
        Assert.Equal(code, result.Code);
        Assert.Single(handler.Calls);
        Assert.False(File.Exists(FilePath));
    }

    private static LlmConfigurationService Create(SettingsService settings, ProviderHandler handler)
    {
        var http = new HttpClient(handler);
        var registry = new LlmProtocolRegistry();
        return new(settings, new LlmConnectionDetector(new(settings, http), registry), new HttpLlmModelCatalog(http, registry));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            foreach (var file in Directory.EnumerateFiles(_directory))
                File.Delete(file);
        if (Directory.Exists(_directory))
            Directory.Delete(_directory);
    }

    private sealed class ProviderHandler(string supported = LlmProtocolIds.OpenAiResponses, bool empty = false,
        bool allProtocols = false, HttpStatusCode? status = null, Action? onRequest = null) : HttpMessageHandler
    {
        public List<(string Protocol, Uri Uri, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var protocol = document.RootElement.TryGetProperty("input", out _) ? LlmProtocolIds.OpenAiResponses
                : document.RootElement.TryGetProperty("system", out _) ? LlmProtocolIds.Anthropic : LlmProtocolIds.OpenAiChat;
            Calls.Add((protocol, request.RequestUri!, body));
            onRequest?.Invoke();
            if (status is { } failure)
                return new(failure) { Content = new StringContent("not accepted") };
            if (!allProtocols && protocol != supported)
                return new(HttpStatusCode.Unauthorized) { Content = new StringContent("not accepted") };
            var payload = JsonSerializer.Serialize(new
            {
                groups = Array.Empty<object>(),
                words = empty ? [] : new[] { new { headword = "日本語", pos = "noun", meaning = "Japanese language", grammar = "A noun naming a language." } },
            });
            var envelope = protocol switch
            {
                LlmProtocolIds.OpenAiResponses => JsonSerializer.Serialize(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = payload } } } } }),
                LlmProtocolIds.Anthropic => JsonSerializer.Serialize(new { content = new[] { new { type = "tool_use", name = "return_groups", input = JsonSerializer.Deserialize<JsonElement>(payload) } } }),
                _ => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = payload } } } }),
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
