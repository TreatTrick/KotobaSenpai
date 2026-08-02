using System.IO;
using KotobaSenpai.App.Logging;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="FileLogger"/> 单元测试：行格式、错误码提取、异常转储、按日滚动、目录自动创建、
/// 级别过滤、并发安全、文件系统失败不抛。用临时目录与可注入时钟隔离。
/// </summary>
public sealed class FileLoggerTests
{
    private static string NewTempDir() => Path.Combine(Path.GetTempPath(), "kslog_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_timestamp_level_and_message()
    {
        var dir = NewTempDir();
        var now = new DateTime(2026, 8, 2, 14, 30, 15);
        using (var logger = new FileLogger(dir, LogLevel.Trace, now: () => now))
        {
            logger.LogInformation("hello {0}", "world");
        }

        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.Contains("2026-08-02T14:30:15", content);
        Assert.Contains("[INF]", content);
        Assert.Contains("hello world", content);
    }

    [Fact]
    public void User_facing_exception_appends_error_code()
    {
        var dir = NewTempDir();
        using (var logger = new FileLogger(dir, LogLevel.Error))
        {
            logger.LogError(new FakeFacingException("OcrLanguagePackMissing"), "recognition failed");
        }

        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.Contains("(ErrorCode=OcrLanguagePackMissing)", content);
        Assert.Contains("recognition failed", content);
    }

    [Fact]
    public void Exception_dump_includes_type_message_and_stack()
    {
        var dir = NewTempDir();
        var ex = MakeExceptionWithStack();
        using (var logger = new FileLogger(dir, LogLevel.Error))
        {
            logger.LogError(ex, "boom");
        }

        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.Contains(ex.GetType().FullName!, content);
        Assert.Contains(ex.Message, content);
        Assert.Contains(ex.StackTrace!, content);
    }

    [Fact]
    public void Rolls_to_new_file_across_dates()
    {
        var dir = NewTempDir();
        var day1 = new DateTime(2026, 8, 1, 10, 0, 0);
        var day2 = new DateTime(2026, 8, 2, 10, 0, 0);
        var clock = new MutableClock(day1);
        using (var logger = new FileLogger(dir, LogLevel.Trace, now: () => clock.Value))
        {
            logger.LogInformation("day1-line");
            clock.Value = day2;
            logger.LogInformation("day2-line");
        }

        var day1Content = File.ReadAllText(Path.Combine(dir, LogRetentionPolicy.FileNameFor(day1)));
        var day2Content = File.ReadAllText(Path.Combine(dir, LogRetentionPolicy.FileNameFor(day2)));

        Assert.Contains("day1-line", day1Content);
        Assert.DoesNotContain("day2-line", day1Content);
        Assert.Contains("day2-line", day2Content);
    }

    [Fact]
    public void Auto_creates_log_directory()
    {
        var dir = Path.Combine(NewTempDir(), "nested", "logs");
        using (var logger = new FileLogger(dir, LogLevel.Trace))
        {
            logger.LogInformation("created");
        }

        Assert.True(Directory.Exists(dir));
        Assert.Contains("created", File.ReadAllText(Directory.GetFiles(dir).Single()));
    }

    [Fact]
    public void Filters_entries_below_minimum_level()
    {
        var dir = NewTempDir();
        using (var logger = new FileLogger(dir, LogLevel.Error))
        {
            logger.LogWarning("WARN_TOKEN_123");
            logger.LogInformation("INFO_TOKEN_123");
            logger.LogError(new Exception("ex-msg"), "ERR_TOKEN_123");
        }

        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.DoesNotContain("WARN_TOKEN_123", content);
        Assert.DoesNotContain("INFO_TOKEN_123", content);
        Assert.Contains("ERR_TOKEN_123", content);
    }

    [Fact]
    public void Keeps_warning_when_minimum_level_is_warning()
    {
        var dir = NewTempDir();
        using (var logger = new FileLogger(dir, LogLevel.Warning))
        {
            logger.LogWarning("WARN_TOKEN");
            logger.LogInformation("INFO_TOKEN");
        }

        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        Assert.Contains("WARN_TOKEN", content);
        Assert.DoesNotContain("INFO_TOKEN", content);
    }

    [Fact]
    public async Task Concurrent_writes_are_all_persisted()
    {
        var dir = NewTempDir();
        using var logger = new FileLogger(dir, LogLevel.Trace);

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => logger.LogInformation("line-{0}", i)));
        await Task.WhenAll(tasks);

        logger.Dispose();
        var content = File.ReadAllText(Directory.GetFiles(dir).Single());
        for (var i = 0; i < 50; i++)
            Assert.Contains($"line-{i}", content);
    }

    [Fact]
    public void Does_not_throw_when_file_system_fails()
    {
        // 把一个文件路径当作日志目录传入 -> 创建目录/写入失败，必须吞掉而非抛给调用方。
        var fileAsDir = Path.Combine(Path.GetTempPath(), "kslog_blocker_" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(fileAsDir, "x");
        try
        {
            var logger = new FileLogger(fileAsDir, LogLevel.Error);
            logger.LogError(new InvalidOperationException("boom"), "msg");
            logger.Dispose(); // 也不抛
        }
        finally
        {
            File.Delete(fileAsDir);
        }
    }

    private static Exception MakeExceptionWithStack()
    {
        try
        {
            throw new InvalidOperationException("stackful failure");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class MutableClock
    {
        public DateTime Value;
        public MutableClock(DateTime initial) => Value = initial;
    }

    private sealed class FakeFacingException : Exception, IUserFacingException
    {
        public FakeFacingException(string errorCode) => ErrorCode = errorCode;
        public string ErrorCode { get; }
    }
}
