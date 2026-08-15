using System.IO;
using KotobaSenpai.App.Logging;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="LogRetentionPolicy"/> unit tests: delete at 8 days, retain at exactly 7 days, retain recent files,
/// never delete non-log files, and fall back to LastWriteTime when the name matches but the date is invalid. Isolated with a temp directory and an injected clock.
/// </summary>
public sealed class LogRetentionPolicyTests
{
    private static string NewTempDir() => Path.Combine(Path.GetTempPath(), "ksret_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Deletes_eight_day_file_retains_seven_day_file()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        var now = new DateTime(2026, 8, 8, 12, 0, 0);

        var eight = Path.Combine(dir, LogRetentionPolicy.FileNameFor(now.AddDays(-8)));
        var seven = Path.Combine(dir, LogRetentionPolicy.FileNameFor(now.AddDays(-7)));
        var recent = Path.Combine(dir, LogRetentionPolicy.FileNameFor(now.AddDays(-1)));
        var other = Path.Combine(dir, "not-a-log.txt");
        File.WriteAllText(eight, "x");
        File.WriteAllText(seven, "x");
        File.WriteAllText(recent, "x");
        File.WriteAllText(other, "x");

        new LogRetentionPolicy(dir).Cleanup(() => now);

        Assert.False(File.Exists(eight));   // 8 days -> delete
        Assert.True(File.Exists(seven));    // exactly 7 days -> retain
        Assert.True(File.Exists(recent));   // recent -> retain
        Assert.True(File.Exists(other));    // non-log file -> not deleted
    }

    [Fact]
    public void Non_matching_files_are_never_deleted_even_when_old()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        var now = new DateTime(2026, 8, 8, 12, 0, 0);

        var oldTxt = Path.Combine(dir, "old.txt");
        File.WriteAllText(oldTxt, "x");
        File.SetLastWriteTime(oldTxt, now.AddDays(-30));

        new LogRetentionPolicy(dir).Cleanup(() => now);

        Assert.True(File.Exists(oldTxt));
    }

    [Fact]
    public void Falls_back_to_last_write_time_for_matched_name_with_invalid_date()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        var now = new DateTime(2026, 8, 8, 12, 0, 0);

        // Matches the naming rule but has an invalid date (month 13); judged by LastWriteTime.
        var oldInvalid = Path.Combine(dir, "kotobasenpai-2026-13-45.log");
        File.WriteAllText(oldInvalid, "x");
        File.SetLastWriteTime(oldInvalid, now.AddDays(-10));

        var recentInvalid = Path.Combine(dir, "kotobasenpai-2026-13-46.log");
        File.WriteAllText(recentInvalid, "x");
        File.SetLastWriteTime(recentInvalid, now);

        new LogRetentionPolicy(dir).Cleanup(() => now);

        Assert.False(File.Exists(oldInvalid));    // old -> delete (fall back to LastWriteTime)
        Assert.True(File.Exists(recentInvalid));  // recent -> retain
    }
}
