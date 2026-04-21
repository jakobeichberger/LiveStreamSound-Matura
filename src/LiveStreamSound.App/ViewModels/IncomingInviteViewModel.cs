using CommunityToolkit.Mvvm.ComponentModel;
using LiveStreamSound.Shared.Localization;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.App.ViewModels;

public partial class IncomingInviteViewModel : ObservableObject
{
    public Invitation Invitation { get; }
    public string HostDisplayName => Invitation.HostDisplayName;
    public string HostAddress => Invitation.HostAddress;
    public string FormattedSessionCode { get; }
    public string BodyText { get; }

    public Loc Localization => Loc.Instance;

    public IncomingInviteViewModel(Invitation inv)
    {
        Invitation = inv;
        FormattedSessionCode = inv.SessionCode.Length == 6
            ? $"{inv.SessionCode[..3]} {inv.SessionCode[3..]}"
            : inv.SessionCode;
        BodyText = string.Format(
            Loc.Instance.Get("Incoming.Body"),
            inv.HostDisplayName,
            inv.HostAddress,
            FormattedSessionCode);
    }
}
