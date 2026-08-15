using System.Globalization;
using System.IO;
using System.Text;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// File implementation of <see cref="ILogger"/>: writes to per-local-date rolling files
/// <c>%LocalAppData%/KotobaSenpai/logs/kotobasenpai-yyyy-MM-dd.log</c>, thread-safe,
/// filtering out entries below the minimum level. Automatically extracts <see cref="IUserFacingException.ErrorCode"/> and appends it to the log line.
/// File-system failures are swallowed; logging never throws to the caller; each write is flushed, so entries written before a crash are not lost.
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

    // Convenience overloads: default interface methods are only available through an interface reference; the same-named methods on the concrete type let the composition root and tests call them directly.
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
        // Cross-midnight rollover triggers cleanup of expired files.
        _retention.Cleanup(_now);
    }

    /// <summary>
    /// Assembles a single-line log entry for writing (exception info is appended on following lines).
    /// Output looks like:
    /// <c>yyyy-MM-ddTHH:mm:ss.fffzzz [LVL] (ErrorCode=…) message text</c>,
    /// and when an exception is present, "type full name: message" and the stack trace are appended after a newline.
    /// </summary>
    /// <param name="level">The log level, mapped to a <c>[TRC]/[INF]/…</c> tag.</param>
    /// <param name="exception">The associated exception; when null only the message is recorded. If it implements <see cref="IUserFacingException"/>, its error code is extracted.</param>
    /// <param name="message">A composite format string, with the same semantics as <see cref="string.Format(IFormatProvider, string, object?[])"/>.</param>
    /// <param name="args">Arguments used to fill the <paramref name="message"/> placeholders; output verbatim when empty.</param>
    /// <returns>The assembled log text (possibly spanning multiple lines).</returns>
    private string FormatEntry(LogLevel level, Exception? exception, string message, params object[] args)
    {
        // Timestamp is fixed to ISO 8601 with a timezone-offset format; InvariantCulture avoids locale influence.
        var timestamp = _now().ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var tag = LevelTag(level);
        // Only when the exception is marked as user-facing is the error code extracted; ordinary exceptions do not get that segment.
        var errorCode = (exception as IUserFacingException)?.ErrorCode;
        var codeSegment = errorCode is null ? string.Empty : $" (ErrorCode={errorCode})";
        // Skip string.Format when there are no arguments, to avoid mis-parsing a literal message containing { } as a format string.
        var formatted = args.Length == 0 ? message : string.Format(CultureInfo.InvariantCulture, message, args);

        var sb = new StringBuilder();
        sb.Append(timestamp).Append(' ').Append(tag).Append(codeSegment).Append(' ').Append(formatted);
        if (exception is not null)
        {
            // Exception info goes on its own line, separated from the first log line; the type full name is preferred so the originating assembly can be located.
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
