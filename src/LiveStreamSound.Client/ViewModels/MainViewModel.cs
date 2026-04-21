using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.Client.Services;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ClientOrchestrator _orchestrator;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty] private string _manualHost = "";
    [ObservableProperty] private int _manualPort = DiscoveryConstants.DefaultControlPort;
    [ObservableProperty] private string _sessionCode = "";
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _connectionError = "";
    [ObservableProperty] private string _connectionErrorBody = "";
    [ObservableProperty] private ConnectionQuality _quality =
        new(0, 0, 0, 0, Array.Empty<ConnectionIssue>(), QualityLevel.Disconnected);
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private ObservableCollection<AudioOutputDevice> _outputDevices = new();
    [ObservableProperty] private string? _selectedOutputDeviceId;
    [ObservableProperty] private string _primaryIssueTitle = "";
    [ObservableProperty] private string _primaryIssueBody = "";
    [ObservableProperty] private bool _hasIssues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private ControlClientState _state = ControlClientState.Idle;

    public bool IsIdle => State is ControlClientState.Idle or ControlClientState.Disconnected or ControlClientState.Failed;
    public bool IsConnected => State == ControlClientState.Connected;
    public bool IsConnecting => State is ControlClientState.Connecting or ControlClientState.Authenticating;
    public bool CanConnect => IsIdle;

    public string ConnectedHostDisplay { get; private set; } = "";

    public ObservableCollection<DiscoveredHostViewModel> DiscoveredHosts { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public Loc Localization => Loc.Instance;

    public MainViewModel(ClientOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _displayName = _orchestrator.SuggestedDisplayName;

        RefreshOutputDevices();

        _orchestrator.Diagnostics.QualityChanged += HandleQualityUpdate;
        _orchestrator.Control.StateChanged += HandleControlStateChange;
        _orchestrator.Control.ConnectionError += err =>
            _dispatcher.BeginInvoke(() => ConnectionError = err);
        _orchestrator.Log.EntryAdded += HandleLogEntry;
        _orchestrator.Discovery.HostsChanged += HandleDiscoveryUpdate;

        foreach (var h in _orchestrator.Discovery.CurrentHosts)
            DiscoveredHosts.Add(new DiscoveredHostViewModel(h));
    }

    public void RefreshOutputDevices()
    {
        OutputDevices.Clear();
        try
        {
            foreach (var d in AudioPlaybackService.EnumerateDevices())
                OutputDevices.Add(d);
        }
        catch (Exception ex)
        {
            _orchestrator.Log.Warn("UI", "Could not enumerate audio devices", ex);
        }
        SelectedOutputDeviceId = OutputDevices.FirstOrDefault(d => d.IsDefault)?.Id
            ?? OutputDevices.FirstOrDefault()?.Id;
    }

    private void HandleControlStateChange(ControlClientState s) =>
        _dispatcher.BeginInvoke(() =>
        {
            State = s;
            if (s == ControlClientState.Connected)
            {
                ConnectedHostDisplay = $"{ManualHost}:{ManualPort}";
                OnPropertyChanged(nameof(ConnectedHostDisplay));
            }
        });

    private void HandleQualityUpdate(ConnectionQuality q)
    {
        _dispatcher.BeginInvoke(() =>
        {
            Quality = q;
            HasIssues = q.ActiveIssues.Count > 0;
            if (HasIssues)
            {
                var top = q.ActiveIssues[0];
                PrimaryIssueTitle = top.Title();
                PrimaryIssueBody = top.Body();
            }
            else { PrimaryIssueTitle = ""; PrimaryIssueBody = ""; }
        });
    }

    private void HandleLogEntry(LogEntry e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LogEntries.Insert(0, e);
            if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    private void HandleDiscoveryUpdate(IReadOnlyList<DiscoveredHost> hosts)
    {
        _dispatcher.BeginInvoke(() =>
        {
            DiscoveredHosts.Clear();
            foreach (var h in hosts)
                DiscoveredHosts.Add(new DiscoveredHostViewModel(h));
        });
    }

    [RelayCommand]
    private void SelectDiscoveredHost(DiscoveredHostViewModel vm)
    {
        ManualHost = vm.Address.ToString();
        ManualPort = vm.ControlPort;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        ConnectionError = "";
        ConnectionErrorBody = "";
        if (!IPAddress.TryParse(ManualHost, out var ip))
        {
            try
            {
                var resolved = await Dns.GetHostAddressesAsync(ManualHost);
                ip = resolved.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                     ?? resolved.FirstOrDefault();
            }
            catch { ip = null; }
        }
        if (ip is null)
        {
            ConnectionError = Loc.Instance.Get("Client.Error.InvalidHost") is { Length: > 0 } v && v != "Client.Error.InvalidHost"
                ? v
                : "Ungültige Host-IP / Invalid host IP";
            return;
        }
        if (string.IsNullOrWhiteSpace(SessionCode))
        {
            ConnectionError = "Kein Code / No code";
            return;
        }

        try
        {
            var ok = await _orchestrator.ConnectAsync(ip, ManualPort, SessionCode.Trim(), DisplayName);
            if (!ok && string.IsNullOrEmpty(ConnectionError))
                ConnectionError = "Verbindung fehlgeschlagen / Connection failed";
        }
        catch (ClientConnectException cx)
        {
            var key = cx.Kind switch
            {
                "audio-port-blocked" => "Client.Error.AudioPortBlocked",
                "no-audio-device" => "Client.Error.NoAudioDevice",
                _ => "Host.Error.Unknown",
            };
            ConnectionError = Loc.Instance.Get(key + ".Title");
            ConnectionErrorBody = Loc.Instance.Get(key + ".Body");
        }
        catch (Exception ex)
        {
            _orchestrator.Log.Error("UI", "Unexpected connect error", ex);
            ConnectionError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await _orchestrator.Control.DisposeAsync();
    }

    [RelayCommand]
    private void SelectDevice(string deviceId)
    {
        try { _orchestrator.Playback.SwitchDevice(deviceId); }
        catch (Exception ex) { _orchestrator.Log.Warn("UI", "Switch device failed", ex); }
    }

    [RelayCommand] private void ToggleLanguage() => Loc.Instance.Toggle();
    [RelayCommand] private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
    [RelayCommand] private void ToggleHelp() => IsHelpOpen = !IsHelpOpen;
    [RelayCommand] private void ToggleLog() => IsLogOpen = !IsLogOpen;

    [RelayCommand]
    private void CloseSidePanels()
    {
        IsHelpOpen = false;
        IsLogOpen = false;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", _orchestrator.Log.LogDirectory); }
        catch { }
    }
    [RelayCommand] private void ClearLogView() => LogEntries.Clear();
}
