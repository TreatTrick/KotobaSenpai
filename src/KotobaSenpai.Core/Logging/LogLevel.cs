namespace KotobaSenpai.Core.Logging;

/// <summary>Log severity levels (low to high). <see cref="ILogger"/> uses these for minimum-level filtering.</summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}
