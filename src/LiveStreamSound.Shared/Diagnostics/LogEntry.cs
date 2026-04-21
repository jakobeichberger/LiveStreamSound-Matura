namespace LiveStreamSound.Shared.Diagnostics;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? ExceptionText = null)
{
    public string Format() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level,-8}] {Category}: {Message}" +
        (ExceptionText is null ? "" : Environment.NewLine + ExceptionText);
}
