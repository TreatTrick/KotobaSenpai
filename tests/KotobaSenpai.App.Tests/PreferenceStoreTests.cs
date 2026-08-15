using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Themes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// Preference-store facade tests: verify typed access and fallback for the language/theme stores via an in-memory fake <see cref="ISettingsService"/>,
/// without touching the disk. File I/O and unknown-field preservation are covered by the <see cref="SettingsService"/> tests.
/// </summary>
public sealed class PreferenceStoreTests
{
    [Fact]
    public void Language_store_round_trips_value()
    {
        var settings = new FakeSettings();
        var store = new LocalAppDataLanguagePreferenceStore(settings);

        store.Save("en");

        Assert.Equal("en", settings.Values["Language"]);
        Assert.Equal("en", store.Load());
    }

    [Fact]
    public void Language_store_treats_absent_as_null()
    {
        var store = new LocalAppDataLanguagePreferenceStore(new FakeSettings());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Language_store_treats_whitespace_as_null()
    {
        var settings = new FakeSettings();
        settings.Values["Language"] = "   ";
        var store = new LocalAppDataLanguagePreferenceStore(settings);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Theme_store_round_trips_value()
    {
        var settings = new FakeSettings();
        var store = new LocalAppDataThemePreferenceStore(settings);

        store.Save(AppThemeMode.Dark);

        Assert.Equal("Dark", settings.Values["Theme"]);
        Assert.Equal(AppThemeMode.Dark, store.Load());
    }

    [Fact]
    public void Theme_store_treats_absent_as_null()
    {
        var store = new LocalAppDataThemePreferenceStore(new FakeSettings());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Theme_store_returns_null_for_unparseable_enum()
    {
        var settings = new FakeSettings();
        settings.Values["Theme"] = "HotPink";
        var store = new LocalAppDataThemePreferenceStore(settings);

        Assert.Null(store.Load());
    }

    private sealed class FakeSettings : ISettingsService
    {
        public Dictionary<string, string?> Values { get; } = new();

        public string? GetValue(string key) =>
            Values.TryGetValue(key, out var v) ? v : null;

        public void SetValue(string key, string? value) => Values[key] = value;
    }
}
