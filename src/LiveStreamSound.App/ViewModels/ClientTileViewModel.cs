using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Protocol;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.App.ViewModels;

public partial class ClientTileViewModel : ObservableObject
{
    private readonly ConnectedClient _model;
    private readonly ControlServer _control;
    private readonly SessionManager _sessions;

    public ConnectedClient Model => _model;
    public string ClientId => _model.ClientId;
    public string RawHostname => _model.ClientName;
    public string DisplayName { get; }
    public LaptopCategory Category { get; }
    public string RoomLabel { get; }

    [ObservableProperty] private float _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private ConnectionQuality _quality;
    [ObservableProperty] private ObservableCollection<OutputDeviceInfo> _availableDevices = new();
    [ObservableProperty] private string? _currentDeviceId;
    [ObservableProperty] private string _primaryIssueTitle = "";
    [ObservableProperty] private string _primaryIssueBody = "";
    [ObservableProperty] private bool _hasIssues;

    public ClientTileViewModel(ConnectedClient model, ControlServer control, SessionManager sessions)
    {
        _model = model;
        _control = control;
        _sessions = sessions;
        var parsed = ClassroomLaptopName.Classify(model.ClientName);
        Category = parsed.Category;
        DisplayName = ClassroomLaptopName.FriendlyName(model.ClientName);
        RoomLabel = string.IsNullOrEmpty(parsed.Room) ? model.ClientName : parsed.Room;
        _volume = model.Volume;
        _isMuted = model.IsMuted;
        _quality = model.CurrentQuality;
        _currentDeviceId = model.CurrentOutputDeviceId;
    }

    partial void OnVolumeChanged(float value) =>
        _ = _control.SendAsync(_model, new SetVolume(Math.Clamp(value, 0f, 1f)));

    partial void OnIsMutedChanged(bool value) =>
        _ = _control.SendAsync(_model, new SetMute(value));

    [RelayCommand]
    private void Kick()
    {
        var result = MessageBox.Show("Client trennen?", "Bestätigen", MessageBoxButton.OKCancel);
        if (result == MessageBoxResult.OK)
        {
            _ = _control.SendAsync(_model, new Kick("Host closed connection"));
            _sessions.UnregisterClient(_model.ClientId);
        }
    }

    [RelayCommand]
    private void SelectDevice(string deviceId)
    {
        CurrentDeviceId = deviceId;
        _ = _control.SendAsync(_model, new SetOutputDevice(deviceId));
    }

    public void UpdateFromModel()
    {
        Volume = _model.Volume;
        IsMuted = _model.IsMuted;
        Quality = _model.CurrentQuality;
        CurrentDeviceId = _model.CurrentOutputDeviceId;
        HasIssues = Quality.ActiveIssues.Count > 0;
        if (HasIssues)
        {
            var top = Quality.ActiveIssues[0];
            PrimaryIssueTitle = top.Title();
            PrimaryIssueBody = top.Body();
        }
        else
        {
            PrimaryIssueTitle = "";
            PrimaryIssueBody = "";
        }
    }

    public void UpdateAvailableDevices(OutputDeviceInfo[] devices)
    {
        AvailableDevices.Clear();
        foreach (var d in devices) AvailableDevices.Add(d);
    }
}
