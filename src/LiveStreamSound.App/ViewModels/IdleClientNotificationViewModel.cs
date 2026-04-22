using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.App.ViewModels;

/// <summary>
/// One toast in the bottom-right corner of the Host dashboard advertising an
/// idle client (mDNS browse hit) that the teacher can add with a single click,
/// without leaving the dashboard or opening the invite dialog.
/// </summary>
public partial class IdleClientNotificationViewModel : ObservableObject
{
    private readonly DiscoveredIdleClient _client;
    private readonly HostOrchestrator _orchestrator;
    private readonly Action<IdleClientNotificationViewModel> _onDismiss;

    [ObservableProperty] private bool _isInviting;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasError;

    public string InstanceName => _client.InstanceName;
    public string DisplayName { get; }
    /// <summary>Two-character avatar shorthand (e.g. "17" for "Raum 17", "WS" for Werkstatt).</summary>
    public string Initial { get; }
    public string AddressLabel => $"{_client.Address}:{_client.IdlePort}";
    public Loc Localization => Loc.Instance;

    public IdleClientNotificationViewModel(
        DiscoveredIdleClient client,
        HostOrchestrator orchestrator,
        Action<IdleClientNotificationViewModel> onDismiss)
    {
        _client = client;
        _orchestrator = orchestrator;
        _onDismiss = onDismiss;

        var rawName = !string.IsNullOrWhiteSpace(client.FriendlyName)
            ? client.FriendlyName
            : client.InstanceName;
        DisplayName = ClassroomLaptopName.FriendlyName(rawName);
        Initial = ComputeInitial(DisplayName, rawName);
    }

    private static string ComputeInitial(string friendly, string raw)
    {
        // Prefer the trailing room number from the friendly name (e.g.
        // "Raum 017" → "17"), since that's how the teacher refers to it
        // out loud. Fall back to the first 2 letters of the raw name.
        var digits = friendly.Where(char.IsDigit).ToArray();
        if (digits.Length >= 2)
            return new string(digits[^Math.Min(2, digits.Length)..]);
        if (digits.Length == 1)
            return $"0{digits[0]}";

        var letters = raw.Where(char.IsLetter).Take(2).ToArray();
        return letters.Length > 0
            ? new string(letters).ToUpperInvariant()
            : "?";
    }

    [RelayCommand(CanExecute = nameof(CanInvite))]
    private async Task Invite()
    {
        if (!_orchestrator.Sessions.IsActive)
        {
            StatusText = Loc.Instance.Get("Host.Status.Idle");
            HasError = true;
            return;
        }
        IsInviting = true;
        InviteCommand.NotifyCanExecuteChanged();
        StatusText = Loc.Instance.Get("Invite.Sending");
        HasError = false;
        try
        {
            var result = await _orchestrator.InviteClient.SendInviteAsync(
                _client.Address,
                _client.IdlePort,
                _orchestrator.Sessions.Code!,
                _orchestrator.LocalIp ?? "",
                _orchestrator.Control.Port,
                Environment.MachineName);

            switch (result.Outcome)
            {
                case InviteOutcome.Accepted:
                    // Auto-dismiss on acceptance — the client will turn into a
                    // tile via the normal HELLO flow within ~1 second.
                    _onDismiss(this);
                    break;
                case InviteOutcome.Declined:
                    StatusText = Loc.Instance.Get("Invite.Error.Rejected");
                    HasError = true;
                    break;
                case InviteOutcome.Unreachable:
                    StatusText = Loc.Instance.Get("Invite.Error.Unreachable");
                    HasError = true;
                    break;
                default:
                    StatusText = result.Reason ?? "error";
                    HasError = true;
                    break;
            }
        }
        finally
        {
            IsInviting = false;
            InviteCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanInvite() => !IsInviting;

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
