using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;

namespace LiveStreamSound.App.ViewModels;

public partial class InviteClientViewModel : ObservableObject
{
    private readonly HostOrchestrator _orchestrator;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty] private string _manualIp = "";
    [ObservableProperty] private int _manualPort = DiscoveryConstants.DefaultIdleClientPort;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isSending;

    public ObservableCollection<IdleClientViewModel> Clients { get; } = new();
    public Loc Localization => Loc.Instance;

    public InviteClientViewModel(HostOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _orchestrator.IdleClientDiscovery.Start();
        _orchestrator.IdleClientDiscovery.ClientsChanged += HandleClientsChanged;
        foreach (var c in _orchestrator.IdleClientDiscovery.CurrentClients)
            Clients.Add(new IdleClientViewModel(c));
    }

    private void HandleClientsChanged(IReadOnlyList<DiscoveredIdleClient> list) =>
        _dispatcher.BeginInvoke(() =>
        {
            Clients.Clear();
            foreach (var c in list) Clients.Add(new IdleClientViewModel(c));
        });

    [RelayCommand]
    private async Task InviteDiscovered(IdleClientViewModel? target)
    {
        if (target is null) return;
        await SendAsync(target.Address, target.Port, target.DisplayName);
    }

    [RelayCommand]
    private async Task InviteManual()
    {
        if (!IPAddress.TryParse(ManualIp.Trim(), out var ip))
        {
            StatusMessage = "IP?";
            return;
        }
        await SendAsync(ip, ManualPort, ManualIp);
    }

    private async Task SendAsync(IPAddress target, int port, string targetLabel)
    {
        if (!_orchestrator.Sessions.IsActive)
        {
            StatusMessage = Loc.Instance.Get("Host.Status.Idle");
            return;
        }
        IsSending = true;
        StatusMessage = Loc.Instance.Get("Invite.Sending");
        try
        {
            var result = await _orchestrator.InviteClient.SendInviteAsync(
                target,
                port,
                _orchestrator.Sessions.Code!,
                _orchestrator.LocalIp ?? "",
                DiscoveryConstants.DefaultControlPort,
                Environment.MachineName);

            StatusMessage = result.Outcome switch
            {
                InviteOutcome.Accepted => $"✓ {targetLabel}",
                InviteOutcome.Declined => Loc.Instance.Get("Invite.Error.Rejected"),
                InviteOutcome.Unreachable => Loc.Instance.Get("Invite.Error.Unreachable"),
                _ => result.Reason ?? "error",
            };
        }
        finally
        {
            IsSending = false;
        }
    }

    public void Dispose()
    {
        _orchestrator.IdleClientDiscovery.ClientsChanged -= HandleClientsChanged;
    }
}
