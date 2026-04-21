using System.Collections.Concurrent;
using System.IO;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Client.Services;

public sealed class LogService : IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly int _capacity;
    private readonly string _logDirectory;
    private readonly object _fileLock = new();
    private StreamWriter? _writer;
    private DateOnly _currentFileDate;
    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyCollection<LogEntry> Recent => _recent.ToArray();
    public string LogDirectory => _logDirectory;

    public LogService(int capacity = 2_000, int retentionDays = 14)
    {
        _capacity = capacity;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(appData, "LiveStreamSound", "LiveStreamSound-Client", "logs");
        Directory.CreateDirectory(_logDirectory);
        CleanOldLogs(TimeSpan.FromDays(retentionDays));
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
        _recent.Enqueue(entry);
        while (_recent.Count > _capacity && _recent.TryDequeue(out _)) { }
        WriteToFile(entry);
        EventLogSink.Write(level, category, message, ex);
        EntryAdded?.Invoke(entry);
    }

    public void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public void Warn(string category, string message, Exception? ex = null) => Log(LogLevel.Warning, category, message, ex);
    public void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);

    private void WriteToFile(LogEntry entry)
    {
        lock (_fileLock)
        {
            var today = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime);
            if (_writer is null || today != _currentFileDate)
            {
                _writer?.Dispose();
                var path = Path.Combine(_logDirectory, $"{today:yyyy-MM-dd}.log");
                _writer = new StreamWriter(path, append: true) { AutoFlush = true };
                _currentFileDate = today;
            }
            _writer.WriteLine(entry.Format());
        }
    }

    public void Dispose()
    {
        lock (_fileLock) { _writer?.Dispose(); _writer = null; }
    }
}
