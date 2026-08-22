using System.Globalization;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Resources;

namespace KotobaSenpai.App.Tests;

public sealed class ResourceManagerStringLocalizerTests
{
    [Fact]
    public void Resolves_active_zhCN_culture_to_chinese_value()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("en"));
        localizer.ApplyCulture(new CultureInfo("zh-CN"));

        Assert.Equal("下划线已隐藏。", localizer.Get(ResourceKeys.Status_Hidden));
    }

    [Fact]
    public void Resolves_active_en_culture_to_english_neutral_value()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("zh-CN"));
        localizer.ApplyCulture(new CultureInfo("en"));

        Assert.Equal("Underline hidden.", localizer.Get(ResourceKeys.Status_Hidden));
    }

    [Fact]
    public void Resolves_pitch_popup_labels_in_supported_cultures()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("en"));

        Assert.Equal("Pitch", localizer.Get("Llm.WordPitchLabel"));
        Assert.Equal("pitch unknown", localizer.Get("Llm.WordPitchUnknown"));

        localizer.ApplyCulture(new CultureInfo("zh-CN"));

        Assert.Equal("音调", localizer.Get("Llm.WordPitchLabel"));
        Assert.Equal("音调未知", localizer.Get("Llm.WordPitchUnknown"));
    }

    [Fact]
    public void Falls_back_to_english_neutral_when_active_culture_has_no_resource()
    {
        // fr-FR has no satellite assembly; the ResourceManager fallback chain lands on neutral English (same mechanism as "a culture lacking that key").
        var localizer = LocalizerFactory.Create(new CultureInfo("zh-CN"));
        localizer.ApplyCulture(new CultureInfo("fr-FR"));

        Assert.Equal("Underline hidden.", localizer.Get(ResourceKeys.Status_Hidden));
    }

    [Fact]
    public void Unknown_key_returns_key_name_observably()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("en"));

        Assert.Equal("DefinitelyNotARealKey", localizer.Get("DefinitelyNotARealKey"));
    }

    [Fact]
    public void Substitutes_format_arguments_into_placeholders()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("zh-CN"));

        Assert.Equal("已选择：测试", localizer.Get(ResourceKeys.Status_Selected, "测试"));
    }

    [Fact]
    public void ApplyCulture_raises_culture_changed_when_culture_actually_changes()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("en"));
        var raised = false;
        localizer.CultureChanged += (_, _) => raised = true;

        localizer.ApplyCulture(new CultureInfo("zh-CN"));

        Assert.True(raised);
    }

    [Fact]
    public void ApplyCulture_does_not_raise_when_culture_is_unchanged()
    {
        var localizer = LocalizerFactory.Create(new CultureInfo("zh-CN"));
        var raised = false;
        localizer.CultureChanged += (_, _) => raised = true;

        localizer.ApplyCulture(new CultureInfo("zh-CN"));

        Assert.False(raised);
    }
}
