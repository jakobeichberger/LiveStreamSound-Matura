using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveStreamSound.Host.Services;

namespace LiveStreamSound.App.ViewModels;

public partial class IdleClientViewModel : ObservableObject
{
    public DiscoveredIdleClient Model { get; }
    public string InstanceName => Model.InstanceName;
    public IPAddress Address => Model.Address;
    public int Port => Model.IdlePort;
    public string DisplayName =>
        !string.IsNullOrEmpty(Model.FriendlyName) ? Model.FriendlyName! :
        ExtractShortName(Model.InstanceName);
    public string DisplayAddress => $"{Model.Address}:{Model.IdlePort}";

    public IdleClientViewModel(DiscoveredIdleClient model) { Model = model; }

    private static string ExtractShortName(string instanceName)
    {
        var dot = instanceName.IndexOf('.');
        return dot > 0 ? instanceName[..dot] : instanceName;
    }
}
