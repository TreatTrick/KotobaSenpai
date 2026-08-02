namespace KotobaSenpai.Core.Logging;

/// <summary>日志严重级别（由低到高）。<see cref="ILogger"/> 据此与最小级别过滤。</summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}
