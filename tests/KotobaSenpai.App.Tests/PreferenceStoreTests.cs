using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Themes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// 偏好存储门面测试：经 in-memory fake <see cref="ISettingsService"/> 验证语言/主题存储的类型化存取与回退，
/// 不触磁盘。文件 I/O 与未知字段保留由 <see cref="SettingsService"/> 测试覆盖。
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
