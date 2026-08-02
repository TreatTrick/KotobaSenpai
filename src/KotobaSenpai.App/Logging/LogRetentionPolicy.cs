using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// 日志保留策略：扫描日志目录，删除日期早于 <see cref="RetentionDays"/> 天的日志文件。
/// 在应用启动、跨日滚动与每小时定时器三处触发。仅删除文件名匹配
/// <c>kotobasenpai-yyyy-MM-dd.log</c> 的旧文件；非日志文件一律不动。
/// 同时作为日志文件命名的唯一来源（<see cref="FileNameFor"/>），避免写入与清理格式漂移。
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

    /// <summary>某日期对应的日志文件名（写入与清理共用，避免格式漂移）。</summary>
    public static string FileNameFor(DateTime date)
        => $"{FileNamePrefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}{FileExtension}";

    /// <summary>扫描并删除过期日志；<paramref name="now"/> 注入便于测试。</summary>
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
            // 仅处理匹配命名规则的文件；非日志文件一律不删。
            if (!TryGetFileDate(Path.GetFileName(file), out var fileDate))
                continue;

            // 恰好 N 天的保留（< cutoff），N+1 天的删除。
            if (fileDate < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>从文件名解析日期；匹配模式但日期非法时回退 LastWriteTime；不匹配返回 false。</summary>
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

        // 匹配文件名但日期非法（如 2026-13-45）：回退 LastWriteTime，仍按过期判断。
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
