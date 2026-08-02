using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;

namespace KotobaSenpai.App.Tests;

public sealed class LanguageServiceTests
{
    [Fact]
    public void Default_uses_chinese_when_os_culture_is_chinese()
    {
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("en")),
            new FakeStore(null),
            () => new CultureInfo("zh-TW"),
            _ => { });

        svc.Initialize();

        Assert.Equal("zh-CN", svc.CurrentCulture.Name);
    }

    [Fact]
    public void Default_uses_english_when_os_culture_is_english()
    {
        var applied = new List<CultureInfo>();
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            new FakeStore(null),
            () => new CultureInfo("en-US"),
            applied.Add);

        svc.Initialize();

        Assert.Equal("en", svc.CurrentCulture.Name);
        Assert.Equal("en", applied[^1].Name);
    }

    [Fact]
    public void Default_uses_english_when_os_culture_is_not_chinese()
    {
        // 系统既非中文也非英文（如法语）时，默认英文（而非简体中文）。
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            new FakeStore(null),
            () => new CultureInfo("fr-FR"),
            _ => { });

        svc.Initialize();

        Assert.Equal("en", svc.CurrentCulture.Name);
    }

    [Fact]
    public void Restore_from_disk_overrides_os_default()
    {
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            new FakeStore("en"),
            () => new CultureInfo("zh-CN"),
            _ => { });

        svc.Initialize();

        Assert.Equal("en", svc.CurrentCulture.Name);
    }

    [Fact]
    public void Corrupt_persisted_preference_falls_through_to_os_default()
    {
        // "xx-XX" 非法文化名 -> 视为无偏好 -> 走 OS 默认。
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            new FakeStore("xx-XX"),
            () => new CultureInfo("en-US"),
            _ => { });

        svc.Initialize();

        Assert.Equal("en", svc.CurrentCulture.Name);
    }

    [Fact]
    public void ChangeCulture_applies_and_persists_user_choice()
    {
        var store = new FakeStore(null);
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            store,
            () => new CultureInfo("zh-CN"),
            _ => { });
        svc.Initialize();

        svc.ChangeCulture(new CultureInfo("en"));

        Assert.Equal("en", svc.CurrentCulture.Name);
        Assert.Equal("en", store.Saved);
    }

    [Fact]
    public void Initialize_syncs_localizer_culture_so_resources_resolve_in_active_language()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("zh-CN"));
        var svc = new LanguageService(localizer, new FakeStore(null), () => new CultureInfo("en-US"), _ => { });

        svc.Initialize();

        Assert.Equal("en", localizer.CurrentCulture.Name);
        Assert.Equal("Underline hidden.", localizer.Get(ResourceKeys.Status_Hidden));
    }

    [Fact]
    public void Available_cultures_lists_zhCN_default_and_en_fallback()
    {
        var svc = new LanguageService(
            LocalizerFactory.Create(new CultureInfo("zh-CN")),
            new FakeStore(null),
            () => new CultureInfo("zh-CN"),
            _ => { });

        Assert.Equal(2, svc.AvailableCultures.Count);
        Assert.Equal("zh-CN", svc.AvailableCultures[0].Name);
        Assert.Equal("en", svc.AvailableCultures[1].Name);
    }

    private sealed class FakeStore : ILanguagePreferenceStore
    {
        private readonly string? _loaded;
        public string? Saved { get; private set; }
        public FakeStore(string? loaded) => _loaded = loaded;
        public string? Load() => _loaded;
        public void Save(string cultureName) => Saved = cultureName;
    }
}
