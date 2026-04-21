using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.App.Services;
using LiveStreamSound.Client.Services;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.App.ViewModels;

public partial class ClientDashboardViewModel : ObservableObject
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

    // Scores 0-1 for the QualityRingControl
    [ObservableProperty] private double _latencyScore;
    [ObservableProperty] private double _lossScore;
    [ObservableProperty] private double _bufferScore;
    [ObservableProperty] private double _overallScore;

    private const int HistoryLength = 30;
    public ObservableCollection<double> RttHistory { get; } = new();
    public ObservableCollection<double> LossHistory { get; } = new();
    public ObservableCollection<double> BufferHistory { get; } = new();

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

    public ClientDashboardViewModel()
    {
        _orchestrator = AppShell.Current.Client
            ?? throw new InvalidOperationException("ClientDashboardViewModel created without active ClientOrchestrator");
        _dispatcher = Dispatcher.CurrentDispatcher;
        _displayName = _orchestrator.SuggestedDisplayName;
        IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        RefreshOutputDevices();

        _orchestrator.Diagnostics.QualityChanged += HandleQualityUpdate;
        _orchestrator.Control.StateChanged += HandleControlStateChange;
        _orchestrator.Control.ConnectionError += err =>
            _dispatcher.BeginInvoke(() => ConnectionError = err);
        _orchestrator.Log.EntryAdded += HandleLogEntry;
        _orchestrator.Discovery.HostsChanged += HandleDiscoveryUpdate;
        _orchestrator.IdleListener.OnInvitation = HandleIncomingInvitation;

        foreach (var h in _orchestrator.Discovery.CurrentHosts)
            DiscoveredHosts.Add(new DiscoveredHostViewModel(h));
    }

    private Task<bool> HandleIncomingInvitation(LiveStreamSound.Shared.Protocol.Invitation inv)
    {
        var tcs = new TaskCompletionSource<bool>();
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                var dlg = new Views.IncomingInviteDialog(inv)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                var accepted = dlg.ShowDialog() == true;
                tcs.SetResult(accepted);

                if (accepted && IPAddress.TryParse(inv.HostAddress, out var ip))
                {
                    ManualHost = inv.HostAddress;
                    ManualPort = inv.HostControlPort;
                    SessionCode = inv.SessionCode;
                    _ = _orchestrator.ConnectAsync(ip, inv.HostControlPort, inv.SessionCode, DisplayName);
                }
            }
            catch (Exception ex)
            {
                _orchestrator.Log.Warn("UI", "Incoming invite dialog failed", ex);
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
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

            LatencyScore = ScoreLatency(q.RoundTripMs);
            LossScore = ScoreLoss(q.PacketLossPercent);
            BufferScore = ScoreBuffer(q.BufferedMs);
            OverallScore = (LatencyScore + LossScore + BufferScore) / 3.0;

            PushHistory(RttHistory, q.RoundTripMs);
            PushHistory(LossHistory, q.PacketLossPercent);
            PushHistory(BufferHistory, q.BufferedMs);
        });
    }

    /// <summary>Latency score: 1.0 at 0ms, 0.0 at 200ms+, linear.</summary>
    public static double ScoreLatency(int rttMs) =>
        Math.Clamp(1.0 - rttMs / 200.0, 0, 1);

    /// <summary>Loss score: 1.0 at 0%, 0.0 at 10%+, linear.</summary>
    public static double ScoreLoss(float lossPct) =>
        Math.Clamp(1.0 - lossPct / 10.0, 0, 1);

    /// <summary>Buffer score: triangular around the 100ms target, 0 at &lt;20 or &gt;400.</summary>
    public static double ScoreBuffer(int bufferMs)
    {
        if (bufferMs < 20 || bufferMs > 400) return 0;
        if (bufferMs <= 100) return (bufferMs - 20) / 80.0;
        return Math.Max(0, 1 - (bufferMs - 100) / 300.0);
    }

    private static void PushHistory(ObservableCollection<double> coll, double value)
    {
        coll.Add(value);
        while (coll.Count > HistoryLength) coll.RemoveAt(0);
    }

    private void HandleLogEntry(LogEntry e) =>
        _dispatcher.BeginInvoke(() =>
        {
            LogEntries.Insert(0, e);
            if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
        });

    private void HandleDiscoveryUpdate(IReadOnlyList<DiscoveredHost> hosts) =>
        _dispatcher.BeginInvoke(() =>
        {
            DiscoveredHosts.Clear();
            foreach (var h in hosts)
                DiscoveredHosts.Add(new DiscoveredHostViewModel(h));
        });

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
        IPAddress? ip = null;
        if (!IPAddress.TryParse(ManualHost, out ip))
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
            ConnectionError = "Ungültige Host-IP / Invalid host IP";
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

    [RelayCommand] private async Task Disconnect() =>
        await _orchestrator.Control.DisposeAsync();

    [RelayCommand]
    private void SelectDevice(string deviceId)
    {
        try { _orchestrator.Playback.SwitchDevice(deviceId); }
        catch (Exception ex) { _orchestrator.Log.Warn("UI", "Switch device failed", ex); }
    }

    [RelayCommand] private void ToggleLanguage() => Loc.Instance.Toggle();
    [RelayCommand]
    private void ToggleTheme()
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

    [RelayCommand]
    private void SwitchRole()
    {
        if (AppShell.Current.HasActiveSession)
        {
            var result = MessageBox.Show(
                Loc.Instance.Get("App.SwitchRoleConfirmBody"),
                Loc.Instance.Get("App.SwitchRoleConfirmTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
        }
        AppShell.Current.ShowRoleSelection();
    }
}
