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

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch
        {
            // Settings persistence is best-effort; failure shouldn't crash the app.
        }
    }
}
