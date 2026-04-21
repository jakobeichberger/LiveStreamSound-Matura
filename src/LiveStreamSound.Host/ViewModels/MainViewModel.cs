using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;
using LiveStreamSound.Shared.Protocol;
using LiveStreamSound.Shared.Session;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.Host.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HostOrchestrator _orchestrator;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<LaptopCategory, ClientGroupViewModel> _groups = new();

    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string? _sessionCode;
    [ObservableProperty] private string _hostIp = "?";
    [ObservableProperty] private int _controlPort = DiscoveryConstants.DefaultControlPort;
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private string _connectionUri = "";
    [ObservableProperty] private bool _audioFlowing;
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private string _startupErrorTitle = "";
    [ObservableProperty] private string _startupErrorBody = "";
    [ObservableProperty] private bool _hasStartupError;
    [ObservableProperty] private string _formattedSessionCode = "";

    public ObservableCollection<ClientGroupViewModel> ClientGroups { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public Loc Localization => Loc.Instance;

    public MainViewModel(HostOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _dispatcher = Dispatcher.CurrentDispatcher;

        foreach (LaptopCategory cat in Enum.GetValues<LaptopCategory>())
            _groups[cat] = new ClientGroupViewModel(cat);

        _orchestrator.Sessions.ClientJoined += OnClientJoined;
        _orchestrator.Sessions.ClientLeft += OnClientLeft;
        _orchestrator.Sessions.SessionStateChanged += OnSessionStateChanged;
        _orchestrator.Diagnostics.ClientQualityUpdated += OnClientQualityUpdated;
        _orchestrator.Control.ClientStatusReceived += OnClientStatus;
        _orchestrator.Log.EntryAdded += OnLogEntry;

        // Poll audio-flowing state every second (lightweight)
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => AudioFlowing = _orchestrator.Pipeline.AudioFlowing,
            _dispatcher);
        timer.Start();
    }

    private void OnSessionStateChanged()
    {
        _dispatcher.Invoke(() =>
        {
            IsSessionActive = _orchestrator.Sessions.IsActive;
            SessionCode = _orchestrator.Sessions.Code;
            FormattedSessionCode = FormatCodeForDisplay(SessionCode ?? "");
            if (!IsSessionActive)
            {
                foreach (var g in _groups.Values) g.Clients.Clear();
                ClientGroups.Clear();
                QrCode = null;
                ConnectionUri = "";
            }
        });
    }

    private void OnClientJoined(ConnectedClient c)
    {
        _dispatcher.Invoke(() =>
        {
            var vm = new ClientViewModel(c, _orchestrator.Control, _orchestrator.Sessions);
            vm.UpdateFromModel();
            var group = _groups[vm.Category];
            group.Clients.Add(vm);
            if (!ClientGroups.Contains(group)) ClientGroups.Add(group);
            _ = _orchestrator.Control.SendAsync(c, new ListOutputDevicesRequest());
        });
    }

    private void OnClientLeft(ConnectedClient c)
    {
        _dispatcher.Invoke(() =>
        {
            foreach (var group in _groups.Values)
            {
                var vm = group.Clients.FirstOrDefault(x => x.ClientId == c.ClientId);
                if (vm is not null)
                {
                    group.Clients.Remove(vm);
                    if (group.Clients.Count == 0) ClientGroups.Remove(group);
                    break;
                }
            }
        });
    }

    private void OnClientQualityUpdated(ConnectedClient c)
    {
        _dispatcher.Invoke(() =>
        {
            var vm = FindVm(c.ClientId);
            vm?.UpdateFromModel();
        });
    }

    private void OnClientStatus(ConnectedClient c, ClientStatus status)
    {
        _dispatcher.Invoke(() =>
        {
            var vm = FindVm(c.ClientId);
            vm?.UpdateFromModel();
        });
    }

    private ClientViewModel? FindVm(string clientId)
    {
        foreach (var g in _groups.Values)
        {
            var vm = g.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (vm is not null) return vm;
        }
        return null;
    }

    private void OnLogEntry(LogEntry e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LogEntries.Insert(0, e);
            if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    [RelayCommand]
    private void StartSession()
    {
        HasStartupError = false;
        StartupErrorTitle = "";
        StartupErrorBody = "";
        try
        {
            _orchestrator.StartSession(Environment.MachineName);
            HostIp = _orchestrator.LocalIp ?? "?";
            ControlPort = DiscoveryConstants.DefaultControlPort;
            FormattedSessionCode = FormatCodeForDisplay(_orchestrator.Sessions.Code ?? "");
            var uri = QrCodeService.BuildConnectionUri(HostIp, ControlPort, _orchestrator.Sessions.Code!);
            ConnectionUri = uri;
            QrCode = QrCodeService.GeneratePng(uri);
        }
        catch (SessionStartException ex)
        {
            var key = ex.Kind switch
            {
                "port-in-use" => "Host.Error.PortInUse",
                "no-audio-device" => "Host.Error.NoAudioDevice",
                "socket-error" => "Host.Error.SocketError",
                _ => "Host.Error.Unknown",
            };
            StartupErrorTitle = Loc.Instance.Get(key + ".Title");
            StartupErrorBody = Loc.Instance.Get(key + ".Body");
            HasStartupError = true;
        }
        catch (Exception ex)
        {
            _orchestrator.Log.Error("UI", "Unexpected start error", ex);
            StartupErrorTitle = Loc.Instance.Get("Host.Error.Unknown.Title");
            StartupErrorBody = Loc.Instance.Get("Host.Error.Unknown.Body");
            HasStartupError = true;
        }
    }

    private static string FormatCodeForDisplay(string code)
    {
        if (code.Length == 6) return $"{code[..3]} {code[3..]}";
        return code;
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        await _orchestrator.StopSessionAsync();
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
        HasStartupError = false;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _orchestrator.Log.LogDirectory);
        }
        catch { /* ignore */ }
    }

    [RelayCommand] private void ClearLogView() => LogEntries.Clear();
}
