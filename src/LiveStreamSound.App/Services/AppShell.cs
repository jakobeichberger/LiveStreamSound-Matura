using System.IO;
using System.Windows;
using LiveStreamSound.App.Views;
using LiveStreamSound.Client.Services;
using LiveStreamSound.Host.Services;

namespace LiveStreamSound.App.Services;

/// <summary>
/// Single source of truth for which top-level window is visible and which role the app currently
/// operates in. Role switching disposes the orchestrator and swaps the window.
/// </summary>
public sealed class AppShell
{
    public static AppShell Current { get; } = new();

    public enum Role { None, Host, Client }
    public Role CurrentRole { get; private set; } = Role.None;

    public HostOrchestrator? Host { get; private set; }
    public ClientOrchestrator? Client { get; private set; }

    /// <summary>Persistent UI/behaviour preferences (technician mode, host auto-mute, …).</summary>
    public UserSettingsService Settings { get; } = new();

    /// <summary>Suppresses Windows sleep/lid-close hibernation while a session is active.</summary>
    public SleepSuppressionService Sleep { get; } = new();

    private Window? _currentWindow;

    public void ShowRoleSelection()
    {
        DisposeCurrentRole();
        Sleep.End();
        CurrentRole = Role.None;
        SwapTo(() => new RoleSelectionWindow());
    }

    public void EnterHostMode()
    {
        try
        {
            DisposeCurrentRole();
            EmergencyLog("EnterHostMode: constructing HostOrchestrator");
            Host = new HostOrchestrator();
            EmergencyLog("EnterHostMode: HostOrchestrator constructed");
            CurrentRole = Role.Host;
            Sleep.Begin();
            SwapTo(() => new HostDashboardWindow());
            EmergencyLog("EnterHostMode: dashboard window swapped in — done");
        }
        catch (Exception ex)
        {
            EmergencyLog("EnterHostMode FAILED", ex);
            ShowFailureMessageBox("Senden", ex);
            throw;
        }
    }

    public void EnterClientMode()
    {
        try
        {
            DisposeCurrentRole();
            EmergencyLog("EnterClientMode: constructing ClientOrchestrator");
            Client = new ClientOrchestrator();
            EmergencyLog("EnterClientMode: ClientOrchestrator constructed");
            Client.Discovery.Start();
            Client.StartIdleListener();
            CurrentRole = Role.Client;
            Sleep.Begin();
            SwapTo(() => new ClientDashboardWindow());
            EmergencyLog("EnterClientMode: dashboard window swapped in — done");
        }
        catch (Exception ex)
        {
            EmergencyLog("EnterClientMode FAILED", ex);
            ShowFailureMessageBox("Empfangen", ex);
            throw;
        }
    }

    /// <summary>
    /// Log to a fallback file BEFORE / INDEPENDENTLY OF the regular LogService —
    /// because if HostOrchestrator construction throws (or LogService itself is
    /// the thing failing), we'd otherwise have no audit trail at all and the
    /// user just sees "click does nothing". This file is the first place to
    /// look when the app silently refuses to start a role.
    /// </summary>
    private static void EmergencyLog(string message, Exception? ex = null)
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

    private static void ShowFailureMessageBox(string roleName, Exception ex)
    {
        var crashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveStreamSound", "crashes");
        var msg =
            $"Konnte nicht in den »{roleName}«-Modus wechseln.\n\n" +
            $"Fehler: {ex.GetType().Name}: {ex.Message}\n\n" +
            $"Detailliertes Log liegt in:\n{crashDir}\n\n" +
            $"Bitte das Log per E-Mail an jakob@eichberger.tech schicken.";
        try
        {
            MessageBox.Show(msg, "LiveStreamSound — Fehler beim Starten",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* MessageBox itself failed; we already have the file log */ }
    }

    private void DisposeCurrentRole()
    {
        try
        {
            if (Host is not null)
            {
                Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Host = null;
            }
            if (Client is not null)
            {
                Client.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Client = null;
            }
        }
        catch { /* best effort — don't block role switch on cleanup errors */ }
    }

    private void SwapTo(Func<Window> factory)
    {
        var newWindow = factory();
        newWindow.Show();
        var old = _currentWindow;
        _currentWindow = newWindow;
        // Defer close to avoid flicker
        old?.Dispatcher.BeginInvoke(() => old.Close());
        Application.Current.MainWindow = newWindow;
    }

    public bool HasActiveSession =>
        (Host is not null && Host.Sessions.IsActive) ||
        (Client is not null && Client.Control.State == ControlClientState.Connected);
}
