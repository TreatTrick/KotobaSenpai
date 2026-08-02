using KotobaSenpai.App.Logging;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="LogConfiguration"/> 单元测试：经 in-memory fake <see cref="ISettingsService"/> 验证
/// 缺省/缺字段回退 Error、解析 "Warning"、非法值回退 Error。文件 I/O 与容错由设置服务承担，本测试不触磁盘。
/// </summary>
public sealed class LogConfigurationTests
{
    [Fact]
    public void Absent_field_defaults_to_error()
    {
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(new FakeSettings(null)));
    }

    [Fact]
    public void Parses_warning_level()
    {
        Assert.Equal(LogLevel.Warning, LogConfiguration.LoadMinimumLevel(new FakeSettings("Warning")));
    }

    [Fact]
    public void Unparseable_value_falls_back_to_error()
    {
        Assert.Equal(LogLevel.Error, LogConfiguration.LoadMinimumLevel(new FakeSettings("Verbose")));
    }

    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _value;
        public FakeSettings(string? value) => _value = value;
        public string? GetValue(string key) => _value;
        public void SetValue(string key, string? value) { }
    }
}
