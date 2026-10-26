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

    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = [];
        public string? GetValue(string key) => _values.GetValueOrDefault(key);
        public void SetValue(string key, string? value) => _values[key] = value;
    }
}
