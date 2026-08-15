using KotobaSenpai.App.Logging;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="LogConfiguration"/> unit tests: via an in-memory fake <see cref="ISettingsService"/>, verify
/// absent-field fallback to Error, parsing "Warning", and invalid-value fallback to Error. File I/O and fault tolerance are handled by the settings service; these tests do not touch the disk.
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
