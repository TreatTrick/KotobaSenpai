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
/// 钉死本地化键无缺口：MainWindow.xaml、MainWindowViewModel（经 ResourceKeys）与 ErrorCodes 中
/// 引用的每个键都必须存在于中性 Strings.resx，避免运行时回退为键名。
/// </summary>
public sealed partial class LocalizationKeyCoverageTests
{
    private static readonly ResourceManager Strings = new(
        "KotobaSenpai.App.Resources.Strings",
        typeof(ResourceKeys).Assembly);

    private static HashSet<string>? _neutralKeys;

    /// <summary>中性 Strings.resx 的全部键。GetResourceSet 返回的是缓存集合，不可释放；故缓存键集只枚举一次。</summary>
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

    [Fact]
    public void Every_xaml_loc_key_has_a_neutral_resource_entry()
    {
        var neutral = NeutralKeys;
        var xaml = File.ReadAllText(FindSourceFile("src/KotobaSenpai.App/MainWindow.xaml"));

        foreach (var match in LocKeyRegex().Matches(xaml).Cast<Match>())
            Assert.Contains(match.Groups[1].Value, neutral);
    }

    /// <summary>从测试输出目录向上查找仓库内的源文件，避免硬编码相对层级。</summary>
    private static string FindSourceFile(string relativePathFromRepo)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePathFromRepo);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"未找到源文件：{relativePathFromRepo}", relativePathFromRepo);
    }

    [GeneratedRegex(@"loc:Loc\s+Key\s*=\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex LocKeyRegex();
}
