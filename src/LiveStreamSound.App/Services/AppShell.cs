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

    private Window? _currentWindow;

    public void ShowRoleSelection()
    {
        DisposeCurrentRole();
        CurrentRole = Role.None;
        SwapTo(() => new RoleSelectionWindow());
    }

    public void EnterHostMode()
    {
        DisposeCurrentRole();
        Host = new HostOrchestrator();
        CurrentRole = Role.Host;
        SwapTo(() => new HostDashboardWindow());
    }

    public void EnterClientMode()
    {
        DisposeCurrentRole();
        Client = new ClientOrchestrator();
        Client.Discovery.Start();
        Client.StartIdleListener();
        CurrentRole = Role.Client;
        SwapTo(() => new ClientDashboardWindow());
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
