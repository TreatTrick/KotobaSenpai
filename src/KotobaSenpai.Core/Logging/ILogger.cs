namespace KotobaSenpai.Core.Logging;

/// <summary>
/// Cross-cutting logging port. It lives in Core (zero external dependencies) with the file implementation in
/// App; the ViewModel depends only on this port, in the same port-adapter style as <c>IStringLocalizer</c>. The
/// port does not reference <c>Core.Localization</c>: extracting the <c>ErrorCode</c> of user-visible exceptions
/// is done by the App implementation, keeping the logging and localization cross-cutting ports decoupled.
/// </summary>
public interface ILogger
{
    /// <summary>Writes a log entry; when <paramref name="exception"/> is non-null its type/message/stack trace are included.</summary>
    void Log(LogLevel level, Exception? exception, string message, params object[] args);

    /// <summary>Error-level convenience overload (records an exception).</summary>
    void LogError(Exception exception, string message, params object[] args)
        => Log(LogLevel.Error, exception, message, args);

    /// <summary>Warning-level convenience overload.</summary>
    void LogWarning(string message, params object[] args)
        => Log(LogLevel.Warning, null, message, args);

    /// <summary>Information-level convenience overload.</summary>
    void LogInformation(string message, params object[] args)
        => Log(LogLevel.Information, null, message, args);
}
