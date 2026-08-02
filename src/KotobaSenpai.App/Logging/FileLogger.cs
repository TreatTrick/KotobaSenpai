using System.Globalization;
using System.IO;
using System.Text;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// <see cref="ILogger"/> 文件实现：按本地日期滚动写入
/// <c>%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log</c>，并发安全，
/// 低于最小级别的条目被过滤。自动提取 <see cref="IUserFacingException.ErrorCode"/> 附加到日志行。
/// 文件系统失败被吞掉，记日志绝不向调用方抛异常；每次写入即刷新，崩溃前已写条目不丢。
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _logsDirectory;
    private readonly LogLevel _minimumLevel;
    private readonly LogRetentionPolicy _retention;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private DateTime _currentDate;
    private StreamWriter? _writer;

    public FileLogger(
        string logsDirectory,
        LogLevel minimumLevel,
        LogRetentionPolicy? retention = null,
        Func<DateTime>? now = null)
    {
        _logsDirectory = logsDirectory;
        _minimumLevel = minimumLevel;
        _retention = retention ?? new LogRetentionPolicy(logsDirectory);
        _now = now ?? (() => DateTime.Now);
    }

    /// <inheritdoc />
    public void Log(LogLevel level, Exception? exception, string message, params object[] args)
    {
        if (level < _minimumLevel)
            return;

        try
        {
            var line = FormatEntry(level, exception, message, args);
            lock (_gate)
            {
                EnsureWriterForToday();
                _writer!.WriteLine(line);
                _writer.Flush();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (FormatException) { }
        // ponytail: logging must never throw to callers; swallowing fs/format failures is intentional.
    }

    // 便捷重载：默认接口方法仅经接口引用可用；在具体类型上提供同名方法，供组合根与测试直接调用。
    public void LogError(Exception exception, string message, params object[] args)
        => Log(LogLevel.Error, exception, message, args);

    public void LogWarning(string message, params object[] args)
        => Log(LogLevel.Warning, null, message, args);

    public void LogInformation(string message, params object[] args)
        => Log(LogLevel.Information, null, message, args);

    private void EnsureWriterForToday()
    {
        var today = _now().Date;
        if (_writer is not null && today == _currentDate)
            return;

        _writer?.Dispose();
        Directory.CreateDirectory(_logsDirectory);
        _currentDate = today;
        _writer = new StreamWriter(Path.Combine(_logsDirectory, LogRetentionPolicy.FileNameFor(today)), append: true);
        // 跨日滚动触发：清理过期文件。
        _retention.Cleanup(_now);
    }

    /// <summary>
    /// 将一条日志组装为待写入的单行（异常信息追加在后续行）。
    /// 输出形如：
    /// <c>yyyy-MM-ddTHH:mm:ss.fffzzz [LVL] (ErrorCode=…) 消息文本</c>，
    /// 若带异常则在换行后依次附加“类型全名: 消息”与堆栈。
    /// </summary>
    /// <param name="level">日志级别，映射为 <c>[TRC]/[INF]/…</c> 标签。</param>
    /// <param name="exception">关联异常；为 null 时仅记录消息。若实现 <see cref="IUserFacingException"/>，则提取其错误码。</param>
    /// <param name="message">复合格式字符串，语义同 <see cref="string.Format(IFormatProvider, string, object?[])"/>。</param>
    /// <param name="args">用于填充 <paramref name="message"/> 占位符的参数；为空时直接原样输出。</param>
    /// <returns>组装完成的日志文本（可能跨多行）。</returns>
    private string FormatEntry(LogLevel level, Exception? exception, string message, params object[] args)
    {
        // 时间戳固定为 ISO 8601 带时区偏移格式，使用InvariantCulture避免区域设置影响。
        var timestamp = _now().ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var tag = LevelTag(level);
        // 仅当异常被标记为面向用户的异常时才提取错误码，普通异常不附加该段。
        var errorCode = (exception as IUserFacingException)?.ErrorCode;
        var codeSegment = errorCode is null ? string.Empty : $" (ErrorCode={errorCode})";
        // 无参时跳过 string.Format，避免把含 { } 的字面消息误当作格式串解析。
        var formatted = args.Length == 0 ? message : string.Format(CultureInfo.InvariantCulture, message, args);

        var sb = new StringBuilder();
        sb.Append(timestamp).Append(' ').Append(tag).Append(codeSegment).Append(' ').Append(formatted);
        if (exception is not null)
        {
            // 异常信息另起一行，与首行日志分隔；优先用类型全名以便定位来源程序集。
            sb.AppendLine();
            sb.Append(exception.GetType().FullName ?? exception.GetType().Name).Append(": ").Append(exception.Message);
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.AppendLine();
                sb.Append(exception.StackTrace);
            }
        }
        return sb.ToString();
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "[TRC]",
        LogLevel.Debug => "[DBG]",
        LogLevel.Information => "[INF]",
        LogLevel.Warning => "[WRN]",
        LogLevel.Error => "[ERR]",
        LogLevel.Critical => "[CRT]",
        _ => "[???]",
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
