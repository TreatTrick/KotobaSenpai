using KotobaSenpai.Core.Errors;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// Pins down that there are no gaps in the localization keys: every key referenced in the shell and page XAML,
/// page view models (via ResourceKeys), and ErrorCodes must exist in the neutral Strings.resx, so the runtime never falls back to the key name.
/// </summary>
public sealed partial class LocalizationKeyCoverageTests
{
    private static readonly ResourceManager Strings = new(
        "KotobaSenpai.App.Resources.Strings",
        typeof(ResourceKeys).Assembly);

    private static HashSet<string>? _neutralKeys;

    /// <summary>All keys in the neutral Strings.resx. GetResourceSet returns a cached collection that must not be disposed; the key set is therefore enumerated only once.</summary>
    private static HashSet<string> NeutralKeys
    {
        get
        {
            if (_neutralKeys is not null)
                return _neutralKeys;

            var set = Strings.GetResourceSet(CultureInfo.InvariantCulture, true, false);
            Assert.NotNull(set);
            _neutralKeys = [.. set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)];
            return _neutralKeys;
        }
    }

    [Fact]
    public void Every_resource_key_constant_has_a_neutral_resource_entry()
    {
        var neutral = NeutralKeys;
        var keys = typeof(ResourceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        foreach (var key in keys)
            Assert.Contains(key, neutral);
    }

    [Fact]
    public void Every_error_code_has_a_localized_message_in_neutral_resources()
    {
        var neutral = NeutralKeys;
        var codes = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        foreach (var code in codes)
            Assert.Contains(code, neutral);
    }

    [Theory]
    [InlineData("src/KotobaSenpai.App/MainWindow.xaml")]
    [InlineData("src/KotobaSenpai.App/Views/RecognitionPage.xaml")]
    [InlineData("src/KotobaSenpai.App/Views/SettingsPage.xaml")]
    public void Every_xaml_loc_key_has_english_and_chinese_resource_entries(string sourcePath)
    {
        var neutral = NeutralKeys;
        var translated = Strings.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false);
        Assert.NotNull(translated);
        var xaml = File.ReadAllText(FindSourceFile(sourcePath));

        foreach (var match in LocKeyRegex().Matches(xaml).Cast<Match>())
        {
            Assert.Contains(match.Groups[1].Value, neutral);
            Assert.NotNull(translated.GetString(match.Groups[1].Value));
        }
    }

    [Fact]
    public void Every_llm_configuration_key_has_a_zh_cn_translation()
    {
        var translated = Strings.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false);
        Assert.NotNull(translated);
        var keys = typeof(ResourceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string)
                && (field.Name.StartsWith("Llm_", StringComparison.Ordinal)
                    || field.Name.StartsWith("Label_Llm", StringComparison.Ordinal)
                    || field.Name.StartsWith("Button_Llm", StringComparison.Ordinal)))
            .Select(field => (string)field.GetRawConstantValue()!);

        foreach (var key in keys)
            Assert.NotNull(translated.GetString(key));
    }

    [Fact]
    public void Empty_recognition_warning_has_distinct_english_and_chinese_translations()
    {
        var english = Strings.GetString("Region_NoJapanese", CultureInfo.GetCultureInfo("en"));
        var chinese = Strings.GetString("Region_NoJapanese", CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("No recognizable Japanese was found in this area. Please select a different area.", english);
        Assert.False(string.IsNullOrWhiteSpace(chinese));
        Assert.NotEqual(english, chinese);
    }

    /// <summary>Searches upward from the test output directory for a source file in the repo, avoiding a hard-coded relative depth.</summary>
    private static string FindSourceFile(string relativePathFromRepo)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePathFromRepo);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"source file not found: {relativePathFromRepo}", relativePathFromRepo);
    }

    [GeneratedRegex(@"loc:Loc\s+Key\s*=\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex LocKeyRegex();
}
