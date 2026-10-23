using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class LlmSettingsViewModelTests
{
    [Fact]
    public void Culture_changes_translate_protocol_choices_without_replacing_the_selected_item()
    {
        var localizer = new FakeLocalizer();
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), new FakeCatalog(), localizer);
        viewModel.Protocol = LlmProtocolIds.OpenAiResponses;
        var selected = viewModel.Protocols.Single(option => option.Id == viewModel.Protocol);
        var previousName = selected.Name;

        localizer.ChangeCulture();

        Assert.Same(selected, viewModel.Protocols.Single(option => option.Id == viewModel.Protocol));
        Assert.NotEqual(previousName, selected.Name);
        Assert.Equal(LlmProtocolIds.OpenAiResponses, viewModel.Protocol);
    }

    [Fact]
    public void Loads_and_persists_flat_settings_while_preserving_other_values()
    {
        var settings = new FakeSettings(new Dictionary<string, string?>
        {
            [LlmSettingsKeys.Enabled] = "true",
            [LlmSettingsKeys.Protocol] = LlmProtocolIds.Anthropic,
            [LlmSettingsKeys.Endpoint] = "https://provider.example",
            [LlmSettingsKeys.ApiKey] = "secret",
            [LlmSettingsKeys.Model] = "model-a",
            ["Theme"] = "Dark",
        });
        using var viewModel = new LlmSettingsViewModel(settings, new FakeCatalog(), new FakeLocalizer());

        Assert.True(viewModel.Enabled);
        Assert.Equal(LlmProtocolIds.Anthropic, viewModel.Protocol);
        Assert.Equal("secret", viewModel.ApiKey);
        viewModel.Endpoint = "https://other.example/v1";
        viewModel.Model = "custom-model";
        viewModel.Enabled = false;

        Assert.Equal("https://other.example/v1", settings.GetValue(LlmSettingsKeys.Endpoint));
        Assert.Equal("custom-model", settings.GetValue(LlmSettingsKeys.Model));
        Assert.Equal("false", settings.GetValue(LlmSettingsKeys.Enabled));
        Assert.Equal("Dark", settings.GetValue("Theme"));
    }

    [Fact]
    public async Task Fetches_models_and_keeps_manual_model_entry_editable()
    {
        var settings = new FakeSettings();
        var catalog = new FakeCatalog
        {
            Result = LlmModelCatalogResult.Success([new("model-a"), new("model-b")]),
        };
        using var viewModel = new LlmSettingsViewModel(settings, catalog, new FakeLocalizer());
        viewModel.Endpoint = "https://provider.example";
        viewModel.ApiKey = "secret";

        await viewModel.FetchModelsCommand.ExecuteAsync(null);
        viewModel.Model = "manual-model";

        Assert.Equal(2, viewModel.Models.Count);
        Assert.Equal("manual-model", settings.GetValue(LlmSettingsKeys.Model));
        Assert.Equal("Llm_ModelsLoaded:2", viewModel.ModelStatus);
    }

    [Fact]
    public async Task Missing_endpoint_is_reported_without_network_call()
    {
        var catalog = new FakeCatalog();
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), catalog, new FakeLocalizer());
        viewModel.ApiKey = "secret";

        await viewModel.FetchModelsCommand.ExecuteAsync(null);

        Assert.Equal(0, catalog.CallCount);
        Assert.Equal("Llm_EndpointRequired", viewModel.ModelStatus);
    }

    [Fact]
    public async Task Missing_api_key_is_reported_without_network_call()
    {
        var catalog = new FakeCatalog();
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), catalog, new FakeLocalizer());
        viewModel.Endpoint = "https://provider.example";

        await viewModel.FetchModelsCommand.ExecuteAsync(null);

        Assert.Equal(0, catalog.CallCount);
        Assert.Equal("Llm_ApiKeyRequired", viewModel.ModelStatus);
    }

    [Fact]
    public async Task Failed_refresh_keeps_previous_models_and_selected_model()
    {
        var catalog = new SequencedCatalog(
            LlmModelCatalogResult.Success([new("model-a")]),
            LlmModelCatalogResult.Failed(LlmModelCatalogFailure.Authentication));
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), catalog, new FakeLocalizer());
        viewModel.Endpoint = "https://provider.example";
        viewModel.ApiKey = "secret";
        viewModel.Model = "model-a";

        await viewModel.FetchModelsCommand.ExecuteAsync(null);
        await viewModel.FetchModelsCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Models);
        Assert.Equal("model-a", viewModel.Model);
        Assert.Equal("Llm_AuthenticationFailed", viewModel.ModelStatus);
    }

    [Fact]
    public async Task A_new_fetch_cancels_and_ignores_the_previous_result()
    {
        var catalog = new SupersededCatalog();
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), catalog, new FakeLocalizer());
        viewModel.Endpoint = "https://provider.example";
        viewModel.ApiKey = "secret";

        var first = viewModel.FetchModelsCommand.ExecuteAsync(null);
        await catalog.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = viewModel.FetchModelsCommand.ExecuteAsync(null);
        await Task.WhenAll(first, second);

        Assert.True(catalog.FirstToken.IsCancellationRequested);
        Assert.Equal("new-model", Assert.Single(viewModel.Models).Id);
    }

    [Fact]
    public async Task Changing_provider_settings_cancels_the_inflight_fetch()
    {
        var catalog = new SupersededCatalog();
        using var viewModel = new LlmSettingsViewModel(new FakeSettings(), catalog, new FakeLocalizer());
        viewModel.Endpoint = "https://provider.example";
        viewModel.ApiKey = "secret";

        var pending = viewModel.FetchModelsCommand.ExecuteAsync(null);
        await catalog.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Endpoint = "https://other.example";
        await pending;

        Assert.True(catalog.FirstToken.IsCancellationRequested);
        Assert.Empty(viewModel.Models);
        Assert.False(viewModel.IsFetchingModels);
        Assert.Empty(viewModel.ModelStatus);
    }

    private sealed class FakeSettings(Dictionary<string, string?>? values = null) : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = values is null ? new() : new(values);
        public string? GetValue(string key) => _values.GetValueOrDefault(key);
        public void SetValue(string key, string? value) => _values[key] = value;
    }

    private sealed class FakeCatalog : ILlmModelCatalog
    {
        public LlmModelCatalogResult Result { get; init; } = LlmModelCatalogResult.Success([]);
        public int CallCount { get; private set; }
        public Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class SequencedCatalog(params LlmModelCatalogResult[] results) : ILlmModelCatalog
    {
        private int _index;
        public Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(results[_index++]);
    }

    private sealed class SupersededCatalog : ILlmModelCatalog
    {
        private int _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken FirstToken { get; private set; }

        public async Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstToken = cancellationToken;
                FirstStarted.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return LlmModelCatalogResult.Success([new("new-model")]);
        }
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
        public event EventHandler? CultureChanged;
        private string _suffix = string.Empty;
        public string Get(string key, params object[] args) => args.Length == 0 ? key + _suffix : $"{key}:{string.Join(',', args)}{_suffix}";
        public void ChangeCulture()
        {
            _suffix = " (translated)";
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
