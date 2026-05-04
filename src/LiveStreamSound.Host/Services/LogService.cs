using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Thread-safe log collector. Maintains an in-memory ring (for the in-app log viewer)
/// and appends to a daily rolling file under %LOCALAPPDATA%\LiveStreamSound\logs\.
/// <para>
/// All file I/O + Event-Log writes happen on a single background consumer task
/// driven by an unbounded <see cref="Channel{LogEntry}"/>. Public Log methods
/// return immediately after enqueueing — at 50 fps the receive loop logging
/// "Invalid packet" no longer serializes on disk fsync. In-memory ring + the
/// EntryAdded UI event still fire synchronously since they're cheap.
/// </para>
/// <para>
/// Rate-limiting: identical (level, category, message) tuples within
/// <see cref="DuplicateSuppressionWindowMs"/> are collapsed (count + first
/// timestamp logged when the burst ends or a different message arrives).
/// </para>
/// </summary>
public sealed class LogService : IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly int _capacity;
    private readonly string _logDirectory;
    private readonly TimeSpan _retention;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _cts = new();
    private StreamWriter? _writer;
    private DateOnly _currentFileDate;
    private int _currentFileSerial; // for size-based rotation within a day
    private string _currentFilePath = "";
    private const long MaxLogFileSizeBytes = 50L * 1024 * 1024; // 50 MB

    private const int DuplicateSuppressionWindowMs = 1000;
    private string? _lastKey;
    private int _suppressedCount;
    private DateTimeOffset _lastFlushAt = DateTimeOffset.UtcNow;

    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyCollection<LogEntry> Recent => _recent.ToArray();
    public string LogDirectory => _logDirectory;

    public LogService(string appName = "LiveStreamSound-Host", int capacity = 2_000, int retentionDays = 14)
    {
        _capacity = capacity;
        _retention = TimeSpan.FromDays(retentionDays);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(appData, "LiveStreamSound", appName, "logs");
        Directory.CreateDirectory(_logDirectory);
        CleanOldLogs(_retention);

        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>Deletes daily rolling-log files older than <paramref name="retention"/>.</summary>
    private void CleanOldLogs(TimeSpan retention)
    {
        try
        {
            var cutoff = DateTime.UtcNow - retention;
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    public void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, message, ex?.ToString());
        // In-memory ring + UI event are cheap and synchronous.
        _recent.Enqueue(entry);
        while (_recent.Count > _capacity && _recent.TryDequeue(out _)) { }
        EntryAdded?.Invoke(entry);
        // File + Event-Log write is offloaded so callers on the audio hot path
        // don't block on disk fsync. TryWrite on an unbounded channel always
        // succeeds; if we shut down mid-flight, the entry is silently dropped.
        _channel.Writer.TryWrite(entry);
    }

    public void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public void Warn(string category, string message, Exception? ex = null) => Log(LogLevel.Warning, category, message, ex);
    public void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (ShouldSuppressDuplicate(entry)) continue;
                    WriteToFile(entry);
                    EventLogSink.Write(entry.Level, entry.Category, entry.Message, null);
                }
                catch
                {
                    // Logger must never throw — swallow and keep going.
                }
            }
        }
        catch (OperationCanceledException) { }
        FlushSuppressedSummary(); // emit any pending duplicate-burst summary on shutdown
    }

    /// <summary>
    /// Collapse repeated identical messages within a short window into a single
    /// "(N× repeated)" summary line. Catches the "Invalid packet received" 50fps
    /// floods and the "UDP send failed" bursts.
    /// </summary>
    private bool ShouldSuppressDuplicate(LogEntry entry)
    {
        var key = $"{entry.Level}|{entry.Category}|{entry.Message}";
        var now = DateTimeOffset.UtcNow;
        if (key == _lastKey && (now - _lastFlushAt).TotalMilliseconds < DuplicateSuppressionWindowMs)
        {
            _suppressedCount++;
            return true;
        }
        FlushSuppressedSummary();
        _lastKey = key;
        _lastFlushAt = now;
        return false;
    }

    private void FlushSuppressedSummary()
    {
        if (_suppressedCount == 0 || _lastKey is null) return;
        var summary = new LogEntry(
            DateTimeOffset.Now, LogLevel.Debug, "LogService",
            $"(previous message repeated {_suppressedCount}× — duplicate-suppressed)",
            null);
        _suppressedCount = 0;
        try { WriteToFile(summary); } catch { }
    }

    private void WriteToFile(LogEntry entry)
    {
        var today = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime);
        // Day rolled over OR file grew beyond the size cap.
        var needsNew = _writer is null || today != _currentFileDate;
        if (!needsNew && _writer is not null && File.Exists(_currentFilePath))
        {
            try { needsNew = new FileInfo(_currentFilePath).Length > MaxLogFileSizeBytes; }
            catch { needsNew = false; }
        }
        if (needsNew)
        {
            _writer?.Dispose();
            if (today != _currentFileDate)
            {
                _currentFileSerial = 0;
                _currentFileDate = today;
            }
            else
            {
                _currentFileSerial++;
            }
            var name = _currentFileSerial == 0
                ? $"{today:yyyy-MM-dd}.log"
                : $"{today:yyyy-MM-dd}-{_currentFileSerial}.log";
            _currentFilePath = Path.Combine(_logDirectory, name);
            _writer = new StreamWriter(_currentFilePath, append: true) { AutoFlush = false };
        }
        _writer!.WriteLine(entry.Format());
        // Periodic flush instead of per-line autoflush — the consumer thread
        // drains the channel; if a batch arrives we can amortise the fsync.
        _writer.Flush();
    }

    public void Dispose()
    {
        try { _channel.Writer.TryComplete(); } catch { }
        try { _cts.Cancel(); } catch { }
        try { _consumerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _cts.Dispose();
    }
}
