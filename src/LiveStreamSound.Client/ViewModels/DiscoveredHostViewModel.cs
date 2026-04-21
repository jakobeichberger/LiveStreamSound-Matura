using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveStreamSound.Client.Services;

namespace LiveStreamSound.Client.ViewModels;

public partial class DiscoveredHostViewModel : ObservableObject
{
    public string InstanceName { get; }
    public IPAddress Address { get; }
    public int ControlPort { get; }
    public string? SessionName { get; }

    public string DisplayAddress => $"{Address}:{ControlPort}";
    public string DisplayName =>
        !string.IsNullOrEmpty(SessionName) ? SessionName! :
        ExtractShortName(InstanceName);

    public DiscoveredHostViewModel(DiscoveredHost model)
    {
        InstanceName = model.InstanceName;
        Address = model.Address;
        ControlPort = model.ControlPort;
        SessionName = model.SessionName;
    }

    private static string ExtractShortName(string instanceName)
    {
        // "LiveStreamSound-HOSTNAME._livestreamsound._tcp.local" → "HOSTNAME"
        var dot = instanceName.IndexOf('.');
        var raw = dot > 0 ? instanceName[..dot] : instanceName;
        const string prefix = "LiveStreamSound-";
        return raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? raw[prefix.Length..]
            : raw;
    }
}
