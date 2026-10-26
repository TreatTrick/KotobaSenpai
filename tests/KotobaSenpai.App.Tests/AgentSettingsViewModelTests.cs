using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

public sealed class AgentSettingsViewModelTests
{
    [Fact]
    public async Task Checking_preserves_connection_fields_and_only_records_verification()
    {
        var settings = new CountingSettings();
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://api.example.com/v1");
        settings.SetValue(LlmSettingsKeys.ApiKey, "saved-test-key");
        settings.SetValue(LlmSettingsKeys.Model, "chosen-model");
        var catalog = new EmptyCatalog();
        var service = new LlmConfigurationService(settings, new SuccessfulDetector(), catalog);
        using var viewModel = new LlmSettingsViewModel(settings, catalog, new ResourceManagerStringLocalizer(), service);
        var before = settings.GetSnapshot(LlmConfiguration.Keys);
        settings.SingleWrites = 0;

        await viewModel.CheckConfigurationCommand.ExecuteAsync(null);

        Assert.Equal(LlmProtocolIds.OpenAiChat, viewModel.Protocol);
        Assert.Equal(before.OrderBy(entry => entry.Key), settings.GetSnapshot(LlmConfiguration.Keys).OrderBy(entry => entry.Key));
        Assert.Equal(0, settings.SingleWrites);
        Assert.Equal(1, settings.BatchWrites);
        Assert.Empty(viewModel.ConfigurationStatus);
        Assert.True(viewModel.IsConfigurationUsable);
        Assert.False(viewModel.IsConfigurationUnavailable);
        using var reopened = new LlmSettingsViewModel(settings, catalog, new ResourceManagerStringLocalizer(), service);
        Assert.True(reopened.IsConfigurationUsable);
        reopened.Model = "another-model";
        Assert.False(reopened.IsConfigurationUsable);
        Assert.Empty(reopened.ConfigurationErrorDetails);
    }

    [Fact]
    public async Task Failed_checks_show_localized_copyable_details_without_credentials_and_clear_on_edit()
    {
        var settings = new CountingSettings();
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://api.example.com/v1");
        settings.SetValue(LlmSettingsKeys.ApiKey, "test-secret");
        settings.SetValue(LlmSettingsKeys.Model, "chosen-model");
        var catalog = new EmptyCatalog();
        var localizer = new ResourceManagerStringLocalizer();
        localizer.ApplyCulture(new System.Globalization.CultureInfo("en"));
        var service = new LlmConfigurationService(settings, new FailedDetector(), catalog);
        using var viewModel = new LlmSettingsViewModel(settings, catalog, localizer, service);

        await viewModel.CheckConfigurationCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConfigurationUsable);
        Assert.True(viewModel.IsConfigurationUnavailable);
        Assert.Equal(localizer.Get("Agent_ConfigurationUnavailable"), viewModel.ConfigurationValidationMessage);
        Assert.Contains("authentication_failed", viewModel.ConfigurationErrorDetails);
        Assert.Contains("HTTP 401", viewModel.ConfigurationErrorDetails);
        Assert.DoesNotContain("test-secret", viewModel.ConfigurationErrorDetails);
        var english = viewModel.ConfigurationErrorDetails;
        localizer.ApplyCulture(new System.Globalization.CultureInfo("zh-CN"));
        Assert.NotEqual(english, viewModel.ConfigurationErrorDetails);
        viewModel.ReportPromptCopy(true);
        Assert.True(viewModel.IsConfigurationUnavailable);
        viewModel.ApiKey = "corrected-test-key";
        Assert.False(viewModel.IsConfigurationUnavailable);
        Assert.Empty(viewModel.ConfigurationErrorDetails);
    }

    [Fact]
    public async Task Editing_during_a_check_cancels_it_and_does_not_show_stale_success()
    {
        var settings = new CountingSettings();
        settings.SetValue(LlmSettingsKeys.Endpoint, "https://api.example.com/v1");
        settings.SetValue(LlmSettingsKeys.ApiKey, "test-secret");
        settings.SetValue(LlmSettingsKeys.Model, "chosen-model");
        var detector = new PendingDetector();
        var catalog = new EmptyCatalog();
        using var viewModel = new LlmSettingsViewModel(settings, catalog, new ResourceManagerStringLocalizer(),
            new LlmConfigurationService(settings, detector, catalog));
        var checking = viewModel.CheckConfigurationCommand.ExecuteAsync(null);
        viewModel.Model = "edited-model";
        detector.Completion.SetResult(new(new(LlmProtocolIds.OpenAiResponses, viewModel.Endpoint, viewModel.ApiKey, "chosen-model")));
        await checking;
        Assert.False(viewModel.IsConfigurationUsable);
        Assert.False(viewModel.IsConfigurationUnavailable);
        Assert.Empty(viewModel.ConfigurationStatus);
        Assert.Equal("edited-model", settings.GetValue(LlmSettingsKeys.Model));
    }

    [Fact]
    public void Prompt_never_reads_saved_credentials_and_missing_tool_reports_a_localized_error()
    {
        var settings = new CountingSettings();
        settings.SetValue(LlmSettingsKeys.ApiKey, "saved-test-key");
        using var viewModel = new LlmSettingsViewModel(settings, new EmptyCatalog(), new ResourceManagerStringLocalizer());
        Assert.DoesNotContain("saved-test-key", viewModel.CreateAgentPrompt(@"C:\App\KotobaSenpai.Cli.exe")!);
        Assert.Null(viewModel.CreateAgentPrompt(null));
        Assert.NotEmpty(viewModel.PromptCopyStatus);
        Assert.False(viewModel.IsPromptCopied);
        Assert.Empty(viewModel.ConfigurationStatus);
    }

    [Fact]
    public void Prompt_copy_feedback_is_localized_and_independent_of_configuration_feedback()
    {
        var localizer = new ResourceManagerStringLocalizer();
        localizer.ApplyCulture(new System.Globalization.CultureInfo("en"));
        using var viewModel = new LlmSettingsViewModel(new CountingSettings(), new EmptyCatalog(), localizer);
        Assert.Empty(viewModel.PromptCopyStatus);

        viewModel.ReportPromptCopy(true);
        Assert.True(viewModel.IsPromptCopied);
        Assert.Equal(localizer.Get("Agent_PromptCopied"), viewModel.PromptCopyStatus);
        viewModel.ReportErrorCopy(true);
        Assert.Equal(localizer.Get("Agent_ErrorCopied"), viewModel.ConfigurationStatus);
        Assert.Equal(localizer.Get("Agent_PromptCopied"), viewModel.PromptCopyStatus);
        localizer.ApplyCulture(new System.Globalization.CultureInfo("zh-CN"));
        Assert.Equal(localizer.Get("Agent_PromptCopied"), viewModel.PromptCopyStatus);
        Assert.True(viewModel.IsPromptCopied);
        viewModel.ReportPromptCopy(false);
        Assert.False(viewModel.IsPromptCopied);
        Assert.Equal(localizer.Get("Agent_CopyFailed"), viewModel.PromptCopyStatus);
    }

    private sealed class SuccessfulDetector : ILlmConnectionDetector
    {
        public Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmDetectionResult(configuration));
        public Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmDetectionResult(candidate with { Protocol = LlmProtocolIds.Anthropic }));
    }

    private sealed class FailedDetector : ILlmConnectionDetector
    {
        public Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
            => DetectAsync(configuration, cancellationToken);
        public Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmDetectionResult(null, "authentication_failed", "HTTP 401: credential test-secret was rejected."));
    }

    private sealed class PendingDetector : ILlmConnectionDetector
    {
        public Task<LlmDetectionResult> CheckAsync(LlmConfiguration configuration, CancellationToken cancellationToken = default)
            => DetectAsync(configuration, cancellationToken);
        public TaskCompletionSource<LlmDetectionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LlmDetectionResult> DetectAsync(LlmConfiguration candidate, CancellationToken cancellationToken = default)
            => Completion.Task;
    }

    private sealed class EmptyCatalog : ILlmModelCatalog
    {
        public Task<LlmModelCatalogResult> GetModelsAsync(LlmModelCatalogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(LlmModelCatalogResult.Success([]));
    }

    private sealed class CountingSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public int SingleWrites { get; set; }
        public int BatchWrites { get; private set; }
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) { SingleWrites++; _values[key] = value; }
        public override bool TryUpdate(IReadOnlyDictionary<string, string?> expected, IReadOnlyDictionary<string, string?> changes)
        {
            if (expected.Any(entry => GetValue(entry.Key) != entry.Value))
                return false;
            foreach (var entry in changes)
                _values[entry.Key] = entry.Value;
            BatchWrites++;
            return true;
        }
    }
}
