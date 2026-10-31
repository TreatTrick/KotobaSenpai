using KotobaSenpai.Core.Dictionary;
using System.Reflection;
using KotobaSenpai.App;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace KotobaSenpai.App.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void Window_shell_can_be_resolved_without_constructing_the_recognition_runtime()
    {
        var services = new ServiceCollection();
        typeof(App).GetMethod("ConfigureServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services]);
        var settings = new MemorySettings();
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(new OnboardingViewModel(settings, new ResourceManagerStringLocalizer(),
            () => new(false, false), _ => Task.CompletedTask, () => Task.CompletedTask));
        services.AddSingleton<RecognitionPageViewModel>(_ => throw new InvalidOperationException("Recognition was initialized too early."));
        using var provider = services.BuildServiceProvider();
        var shell = provider.GetRequiredService<MainWindowViewModel>();
        Assert.Null(shell.Recognition);
        Assert.Null(shell.Selection);
        Assert.Same(provider.GetRequiredService<SettingsPageViewModel>(), shell.Settings);
        shell.Dispose();
    }

    [Fact]
    public void Settings_page_shares_application_preferences_and_llm_state()
    {
        var services = new ServiceCollection();
        var configure = typeof(App).GetMethod("ConfigureServices", BindingFlags.Static | BindingFlags.NonPublic)!;
        configure.Invoke(null, [services]);
        var settings = new MemorySettings();
        services.AddSingleton<ISettingsService>(settings);
        using var provider = services.BuildServiceProvider();

        var page = provider.GetRequiredService<SettingsPageViewModel>();
        page.SelectedTheme = AppThemeMode.Dark;

        Assert.Same(provider.GetRequiredService<LanguageService>(), page.Language);
        Assert.Same(provider.GetRequiredService<ILanguagePreferences>(), page.Language);
        Assert.Same(provider.GetRequiredService<IExplanationLanguagePreferences>(), page.Language);
        Assert.Same(provider.GetRequiredService<LlmSettingsViewModel>(), page.Llm);
        Assert.Equal(AppThemeMode.Dark, provider.GetRequiredService<IThemePreferenceStore>().Load());
        Assert.Equal("Dark", settings.GetValue("Theme"));
        page.ExplanationLanguage = "French";
        page.SaveExplanationLanguageCommand.Execute(null);
        Assert.Equal("French", provider.GetRequiredService<IExplanationLanguagePreferences>().EffectiveLanguage);
        Assert.Equal("French", settings.GetValue("ExplanationLanguage"));
    }

    [Fact]
    public async Task Shutdown_waits_for_cancelled_preparation_to_finish_cleanup()
    {
        var services = new ServiceCollection();
        typeof(App).GetMethod("ConfigureServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services]);
        var settings = new MemorySettings();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(new OnboardingViewModel(settings, new ResourceManagerStringLocalizer(), () => new(false, false),
            async token => { entered.SetResult(); await cleanup.Task; token.ThrowIfCancellationRequested(); }, () => Task.CompletedTask));
        using var provider = services.BuildServiceProvider();
        var shell = provider.GetRequiredService<MainWindowViewModel>();
        var initialization = shell.Onboarding.InitializeCommand.ExecuteAsync(null);
        await entered.Task;
        var stopping = shell.StopAsync();
        var returnedBeforeCleanup = stopping.IsCompleted;
        cleanup.SetResult();
        await stopping;
        await initialization;
        Assert.False(returnedBeforeCleanup);
    }

    [Fact]
    public void Registers_one_shared_dictionary_lookup_for_single_and_batch_ports()
    {
        var services = new ServiceCollection();
        var configure = typeof(App).GetMethod(
            "ConfigureServices",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(configure);

        configure!.Invoke(null, [services]);
        using var provider = services.BuildServiceProvider();

        var single = provider.GetRequiredService<IDictionaryLookup>();
        var batch = provider.GetRequiredService<IBatchDictionaryLookup>();
        var resolver = provider.GetRequiredService<ITokenSpanResolver>();

        Assert.Same(single, batch);
        Assert.IsType<KotobaSenpai.Core.Dictionary.TokenBoundarySpanResolver>(resolver);
    }

    private sealed class MemorySettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }
}
