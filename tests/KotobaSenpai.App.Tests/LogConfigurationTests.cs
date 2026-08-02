using System.IO;
using KotobaSenpai.App.Logging;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="LogConfiguration"/> 单元测试：缺省/缺字段回退 Error、解析 "Warning"、
/// 非法值与损坏 JSON 回退 Error。验证 settings.json 缝隙的可配置最小级别。
/// </summary>
public sealed class LogConfigurationTests
{
    private static string WriteSettings(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "kscfg_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Missing_file_defaults_to_error()
    {
        var path = Path.Combine(Path.GetTempPath(), "kscfg_missing_" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(path));
    }

    [Fact]
    public void Absent_field_defaults_to_error()
    {
        var path = WriteSettings(@"{""Language"":""zh-CN""}");
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(path));
    }

    [Fact]
    public void Parses_warning_level()
    {
        var path = WriteSettings(@"{""MinimumLogLevel"":""Warning""}");
        Assert.Equal(LogLevel.Warning, LogConfiguration.LoadMinimumLevel(path));
    }

    [Fact]
    public void Unparseable_value_falls_back_to_error()
    {
        var path = WriteSettings(@"{""MinimumLogLevel"":""Verbose""}");
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(path));
    }

    [Fact]
    public void Malformed_json_falls_back_to_error()
    {
        var path = WriteSettings(@"{not valid json");
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(path));
    }
}
