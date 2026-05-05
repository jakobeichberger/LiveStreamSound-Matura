using System.IO;

namespace LiveStreamSound.App.Services;

/// <summary>
/// Self-contained "log to disk no matter what" helper. Doesn't depend on
/// LogService (which is itself a candidate for being the failing component).
/// Always writes to <c>%LOCALAPPDATA%\LiveStreamSound\crashes\emergency-YYYY-MM-DD.log</c>.
/// Never throws — silently absorbs all errors so a logging failure can't
/// cascade into a visible app crash.
/// </summary>
public static class EmergencyLog
{
    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveStreamSound", "crashes");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"emergency-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            if (ex is not null) line += Environment.NewLine + ex.ToString();
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* never throw from emergency logger */ }
    }
}
