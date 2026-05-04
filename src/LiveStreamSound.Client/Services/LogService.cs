using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Thread-safe log collector. Maintains an in-memory ring (for the in-app log viewer)
/// and appends to a daily rolling file under %LOCALAPPDATA%\LiveStreamSound\logs\.
/// File I/O happens on a background consumer task driven by a Channel —
/// see Host.LogService for the same pattern + duplicate-suppression rationale.
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
    private int _currentFileSerial;
    private string _currentFilePath = "";
    private const long MaxLogFileSizeBytes = 50L * 1024 * 1024;

    private const int DuplicateSuppressionWindowMs = 1000;
    private string? _lastKey;
    private int _suppressedCount;
    private DateTimeOffset _lastFlushAt = DateTimeOffset.UtcNow;

    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyCollection<LogEntry> Recent => _recent.ToArray();
    public string LogDirectory => _logDirectory;

    public LogService(int capacity = 2_000, int retentionDays = 14)
    {
        _capacity = capacity;
        _retention = TimeSpan.FromDays(retentionDays);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(appData, "LiveStreamSound", "LiveStreamSound-Client", "logs");
        Directory.CreateDirectory(_logDirectory);
        CleanOldLogs(_retention);

        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

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
                catch { }
            }
        }
        catch { }
    }

    public void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, message, ex?.ToString());
        _recent.Enqueue(entry);
        while (_recent.Count > _capacity && _recent.TryDequeue(out _)) { }
        EntryAdded?.Invoke(entry);
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
                catch { }
            }
        }
        catch (OperationCanceledException) { }
        FlushSuppressedSummary();
    }

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
