using System.IO;
using System.Text.Json;

namespace LiveStreamSound.App.Services;

/// <summary>
/// Tiny per-user JSON settings file: <c>%LOCALAPPDATA%\LiveStreamSound\settings.json</c>.
/// Just enough plumbing to persist UI-mode toggles across launches without
/// pulling in a config framework.
/// </summary>
public sealed class UserSettingsService
{
    public sealed class UserSettings
    {
        /// <summary>If true, the Client dashboard shows the technician view
        /// (sparklines, raw metrics, log) instead of the simple status pane.</summary>
        public bool ClientTechnicianMode { get; set; }

        /// <summary>If true, the Host auto-mutes its system audio output when
        /// starting a session (restored on stop).</summary>
        public bool HostAutoMuteOnSessionStart { get; set; } = true;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    public UserSettings Current { get; private set; } = new();

    public UserSettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveStreamSound");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOpts);
            if (loaded is not null) Current = loaded;
        }
        catch
        {
            // Corrupt settings file → start fresh, don't block app launch.
            Current = new UserSettings();
        }
    }

    /// <summary>
    /// Atomic save: write to a sibling .tmp file, then File.Move over the
    /// target. On NTFS this is atomic on the same volume — a crash mid-write
    /// can never produce a half-truncated settings file.
    /// </summary>
    public void Save()
    {
        _saveLock.Wait();
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            // Move with overwrite (NTFS atomic on same volume).
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Settings persistence is best-effort; failure shouldn't crash the app.
            // Cleanup any leftover tmp file.
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
