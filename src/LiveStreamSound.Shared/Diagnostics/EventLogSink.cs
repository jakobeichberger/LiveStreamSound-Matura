namespace LiveStreamSound.Shared.Diagnostics;

/// <summary>
/// Thin wrapper around the Windows Event Log so Warnings / Errors / Criticals
/// from both Host and Client LogServices land in a dedicated "LiveStreamSound"
/// event log (visible in eventvwr.msc under that name).
///
/// The log + source is normally pre-created by the MSI installer (so no admin
/// rights are needed at runtime). If the source doesn't exist and the current
/// process can't create it, this sink silently falls back to a no-op — the
/// rolling file log is still written either way.
/// </summary>
public static class EventLogSink
{
    public const string LogName = "LiveStreamSound";
    public const string SourceName = "LiveStreamSound";

    private static bool _available;
    private static bool _probed;
    private static readonly object _syncRoot = new();

    /// <summary>Called once at app startup. Tries to verify / create the source.</summary>
    public static void Init()
    {
        if (_probed) return;
        lock (_syncRoot)
        {
            if (_probed) return;
            _probed = true;
            _available = TryEnsureSource();
        }
    }

    private static bool TryEnsureSource()
    {
        if (!OperatingSystem.IsWindows()) return false;
#pragma warning disable CA1416 // OperatingSystem.IsWindows already guards

        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(SourceName))
            {
                // Requires admin. If the MSI pre-registered the source this
                // branch is skipped; if not (running uninstalled), creating
                // the source will throw for non-admin users — caught below.
                System.Diagnostics.EventLog.CreateEventSource(
                    new System.Diagnostics.EventSourceCreationData(SourceName, LogName));
            }
            return true;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1416
    }

    public static void Write(LogLevel level, string category, string message, Exception? ex = null)
    {
        if (!_probed) Init();
        if (!_available) return;
        if (!OperatingSystem.IsWindows()) return;
        if (level is LogLevel.Trace or LogLevel.Debug or LogLevel.Info) return; // file only

#pragma warning disable CA1416 // OperatingSystem.IsWindows already guarded above
        try
        {
            var entryType = level switch
            {
                LogLevel.Warning => System.Diagnostics.EventLogEntryType.Warning,
                LogLevel.Error or LogLevel.Critical => System.Diagnostics.EventLogEntryType.Error,
                _ => System.Diagnostics.EventLogEntryType.Information,
            };
            var text = ex is null ? $"[{category}] {message}" : $"[{category}] {message}{Environment.NewLine}{ex}";
            // EventLog entry message is capped at ~31839 chars — truncate to be safe.
            if (text.Length > 30_000) text = text[..30_000] + "…(truncated)";
            using var log = new System.Diagnostics.EventLog(LogName) { Source = SourceName };
            log.WriteEntry(text, entryType);
        }
        catch
        {
            // Swallow: the file log is still the authoritative record.
        }
#pragma warning restore CA1416
    }
}
