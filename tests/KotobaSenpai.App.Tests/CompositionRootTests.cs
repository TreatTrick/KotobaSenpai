using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.App.Composition;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using KotobaSenpai.App.Reading;
using KotobaSenpai.App.Startup;
using KotobaSenpai.Core.Workflow;

namespace KotobaSenpai.App.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void Window_shell_can_be_resolved_without_constructing_the_recognition_runtime()
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        var settings = new MemorySettings();
        services.AddSingleton<ISettingsService>(settings);
        services.AddScoped<RecognitionPageViewModel>(_ => throw new InvalidOperationException("Recognition was initialized too early."));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var shell = scoped.GetRequiredService<MainWindowViewModel>();
        Assert.Null(shell.Recognition);
        Assert.Null(shell.Selection);
        Assert.Same(scoped.GetRequiredService<SettingsPageViewModel>(), shell.Settings);
        Assert.Same(scoped.GetRequiredService<OnboardingViewModel>(), shell.Onboarding);
        Assert.Null(scoped.GetRequiredService<RecognitionStartup>().Current);
        shell.Dispose();
    }

    [Fact]
    public void Window_scopes_isolate_page_state_and_share_application_preferences()
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        services.AddSingleton<ISettingsService>(new MemorySettings());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstShell = first.ServiceProvider.GetRequiredService<MainWindowViewModel>();
        var secondShell = second.ServiceProvider.GetRequiredService<MainWindowViewModel>();

        Assert.Same(firstShell, first.ServiceProvider.GetRequiredService<MainWindowViewModel>());
        Assert.NotSame(firstShell, secondShell);
        Assert.NotSame(firstShell.Onboarding, secondShell.Onboarding);
        Assert.NotSame(firstShell.Settings.Llm, secondShell.Settings.Llm);
        Assert.Same(firstShell.Settings.Language, secondShell.Settings.Language);
        Assert.Same(first.ServiceProvider.GetRequiredService<CaptureBorderPreferences>(), second.ServiceProvider.GetRequiredService<CaptureBorderPreferences>());
        Assert.Same(first.ServiceProvider.GetRequiredService<SentenceTranslationPreferences>(), second.ServiceProvider.GetRequiredService<SentenceTranslationPreferences>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MainWindowViewModel>());
    }

    [Theory]
    [InlineData(typeof(RecognitionSessionController))]
    [InlineData(typeof(ReadingActions))]
    [InlineData(typeof(CaptureBorderPreferences))]
    [InlineData(typeof(SentenceTranslationPreferences))]
    public void Missing_required_dependencies_fail_validation_instead_of_creating_fallbacks(Type dependency)
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        services.RemoveAll(dependency);

        var exception = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        Assert.Contains(dependency.FullName!, exception.ToString());
    }

    [Fact]
    public void Settings_page_shares_application_preferences_and_llm_state()
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        var settings = new MemorySettings();
        services.AddSingleton<ISettingsService>(settings);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        var page = scoped.GetRequiredService<SettingsPageViewModel>();
        page.SelectedTheme = AppThemeMode.Dark;

        Assert.Same(scoped.GetRequiredService<LanguageService>(), page.Language);
        Assert.Same(scoped.GetRequiredService<ILanguagePreferences>(), page.Language);
        Assert.Same(scoped.GetRequiredService<IExplanationLanguagePreferences>(), page.Language);
        Assert.Same(scoped.GetRequiredService<LlmSettingsViewModel>(), page.Llm);
        Assert.Equal(AppThemeMode.Dark, scoped.GetRequiredService<IThemePreferenceStore>().Load());
        Assert.Equal("Dark", settings.GetValue("Theme"));
        page.ExplanationLanguage = "French";
        page.SaveExplanationLanguageCommand.Execute(null);
        Assert.Equal("French", scoped.GetRequiredService<IExplanationLanguagePreferences>().EffectiveLanguage);
        Assert.Equal("French", settings.GetValue("ExplanationLanguage"));
    }

    [Fact]
    public async Task Shutdown_waits_for_cancelled_preparation_to_finish_cleanup()
    {
        var services = new ServiceCollection();
        DesktopComposition.ConfigureServices(services);
        var settings = new MemorySettings();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(new OnboardingViewModel(settings, new ResourceManagerStringLocalizer(), () => new(false, false),
            async token => { entered.SetResult(); await cleanup.Task; token.ThrowIfCancellationRequested(); }, () => Task.CompletedTask));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var shell = scoped.GetRequiredService<MainWindowViewModel>();
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
        DesktopComposition.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        var single = scoped.GetRequiredService<IDictionaryLookup>();
        var batch = scoped.GetRequiredService<IBatchDictionaryLookup>();
        var resolver = scoped.GetRequiredService<ITokenSpanResolver>();

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
