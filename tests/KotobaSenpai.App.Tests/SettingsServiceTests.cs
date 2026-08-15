using System.IO;
using KotobaSenpai.App.Settings;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="SettingsService"/> unit tests: as the sole owner of settings.json, covering missing/corrupt-file fault tolerance,
/// absent-key fallback, write-through round-trips, preserving unknown fields, auto-creating the directory, and not re-reading the file after lazy load.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "ksset_" + Guid.NewGuid().ToString("N") + ".json");
        _tempPaths.Add(path);
        return path;
    }

    [Fact]
    public void Missing_file_returns_null_and_does_not_throw()
    {
        var svc = new SettingsService(NewPath());

        var ex = Record.Exception(() => Assert.Null(svc.GetValue("Language")));

        Assert.Null(ex);
    }

    [Fact]
    public void Corrupt_json_returns_null_and_does_not_throw()
    {
        var path = NewPath();
        File.WriteAllText(path, "{not valid json");
        var svc = new SettingsService(path);

        var ex = Record.Exception(() => Assert.Null(svc.GetValue("Language")));

        Assert.Null(ex);
    }

    [Fact]
    public void Corrupt_json_then_setvalue_writes_valid_file()
    {
        var path = NewPath();
        File.WriteAllText(path, "{not valid json");
        var svc = new SettingsService(path);

        svc.SetValue("Language", "en");

        Assert.Equal("en", svc.GetValue("Language"));
        var content = File.ReadAllText(path);
        Assert.Contains("\"Language\"", content);
        Assert.Contains("en", content);
    }

    [Fact]
    public void Absent_key_returns_null()
    {
        var path = NewPath();
        File.WriteAllText(path, @"{""Theme"":""Dark""}");
        var svc = new SettingsService(path);

        Assert.Null(svc.GetValue("Language"));
    }

    [Fact]
    public void Non_string_value_returns_null()
    {
        var path = NewPath();
        File.WriteAllText(path, @"{""Language"":123}");
        var svc = new SettingsService(path);

        Assert.Null(svc.GetValue("Language"));
    }

    [Fact]
    public void Setvalue_then_getvalue_round_trips()
    {
        var svc = new SettingsService(NewPath());

        svc.SetValue("Language", "zh-CN");

        Assert.Equal("zh-CN", svc.GetValue("Language"));
    }

    [Fact]
    public void Writing_one_key_preserves_others()
    {
        var path = NewPath();
        File.WriteAllText(path, @"{""Language"":""zh-CN"",""Theme"":""Dark"",""MinimumLogLevel"":""Warning""}");
        var svc = new SettingsService(path);

        svc.SetValue("Language", "en");

        Assert.Equal("en", svc.GetValue("Language"));
        Assert.Equal("Dark", svc.GetValue("Theme"));
        Assert.Equal("Warning", svc.GetValue("MinimumLogLevel"));
    }

    [Fact]
    public void Directory_is_auto_created()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ksset_dir_" + Guid.NewGuid().ToString("N"));
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "nested", "settings.json");

        var svc = new SettingsService(path);
        svc.SetValue("Language", "en");

        Assert.True(File.Exists(path));
        // A new instance re-reads from disk, verifying the write-through persisted.
        Assert.Equal("en", new SettingsService(path).GetValue("Language"));
    }

    [Fact]
    public void Repeated_getvalue_does_not_re_read_file()
    {
        var path = NewPath();
        File.WriteAllText(path, @"{""Language"":""en""}");
        var svc = new SettingsService(path);

        Assert.Equal("en", svc.GetValue("Language"));

        // An external process rewrites the file; the service should return the old value from its in-memory view, proving it does not re-read after lazy load.
        File.WriteAllText(path, @"{""Language"":""zh-CN""}");

        Assert.Equal("en", svc.GetValue("Language"));
    }

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
