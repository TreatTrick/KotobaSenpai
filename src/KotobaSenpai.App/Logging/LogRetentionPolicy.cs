using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// Log retention policy: scans the log directory and deletes log files older than <see cref="RetentionDays"/> days.
/// Triggered at app startup, on cross-midnight rollover, and by the hourly timer. Only old files whose names match
/// <c>kotobasenpai-yyyy-MM-dd.log</c> are deleted; non-log files are never touched.
/// Also the single source of log-file naming (<see cref="FileNameFor"/>), so write and cleanup formats cannot drift.
/// </summary>
public sealed class LogRetentionPolicy
{
    internal const string FileNamePrefix = "kotobasenpai-";
    internal const string DateFormat = "yyyy-MM-dd";
    internal const string FileExtension = ".log";

    private static readonly Regex FileNamePattern =
        new(@"^kotobasenpai-(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.Compiled);

    private readonly string _logsDirectory;
    private readonly int _retentionDays;

    public LogRetentionPolicy(string logsDirectory, int retentionDays = 7)
    {
        _logsDirectory = logsDirectory;
        _retentionDays = retentionDays;
    }

    public int RetentionDays => _retentionDays;

    /// <summary>The log file name for a given date (shared by write and cleanup to avoid format drift).</summary>
    public static string FileNameFor(DateTime date)
        => $"{FileNamePrefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}{FileExtension}";

    /// <summary>Scans for and deletes expired logs; <paramref name="now"/> is injected to make it testable.</summary>
    public void Cleanup(Func<DateTime>? now = null)
    {
        var reference = (now ?? (() => DateTime.Now))();
        var cutoff = reference.Date.AddDays(-_retentionDays);

        string[] files;
        try
        {
            files = Directory.GetFiles(_logsDirectory);
        }
        catch (DirectoryNotFoundException) { return; }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var file in files)
        {
            // Only process files matching the naming rule; non-log files are never deleted.
            if (!TryGetFileDate(Path.GetFileName(file), out var fileDate))
                continue;

            // Exactly N days are kept (< cutoff), N+1 days are deleted.
            if (fileDate < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Parses the date from the file name; falls back to LastWriteTime when the pattern matches but the date is invalid; returns false when there is no match.</summary>
    private bool TryGetFileDate(string fileName, out DateTime fileDate)
    {
        var match = FileNamePattern.Match(fileName);
        if (!match.Success)
        {
            fileDate = default;
            return false;
        }

        if (DateTime.TryParseExact(match.Groups[1].Value, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            fileDate = parsed;
            return true;
        }

        // Name matches but the date is invalid (e.g. 2026-13-45): fall back to LastWriteTime and still apply the expiry check.
        try
        {
            fileDate = File.GetLastWriteTime(Path.Combine(_logsDirectory, fileName));
            return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        fileDate = default;
        return false;
    }
}
