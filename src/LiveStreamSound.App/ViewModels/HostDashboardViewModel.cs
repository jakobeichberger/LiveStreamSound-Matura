using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.App.Services;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Localization;
using LiveStreamSound.Shared.Protocol;
using LiveStreamSound.Shared.Session;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.App.ViewModels;

public partial class HostDashboardViewModel : ObservableObject
{
    private readonly HostOrchestrator _orchestrator;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<LaptopCategory, ClientGroupViewModel> _groups = new();

    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string? _sessionCode;
    [ObservableProperty] private string _hostIp = "?";
    [ObservableProperty] private int _controlPort = DiscoveryConstants.DefaultControlPort;
    [ObservableProperty] private bool _audioFlowing;
    [ObservableProperty] private double _audioLevel;
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private string _startupErrorTitle = "";
    [ObservableProperty] private string _startupErrorBody = "";
    [ObservableProperty] private bool _hasStartupError;
    [ObservableProperty] private string _formattedSessionCode = "";
    /// <summary>
    /// Persisted toggle: when true, the host mutes its own speakers on session
    /// start so the teacher's laptop doesn't double-up with the beamer audio.
    /// </summary>
    [ObservableProperty] private bool _autoMuteHostMonitor;

    public ObservableCollection<ClientGroupViewModel> ClientGroups { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    /// <summary>Animated toasts in the bottom-right corner: idle clients on the LAN
    /// that the teacher can add with one click.</summary>
    public ObservableCollection<IdleClientNotificationViewModel> IdleClientNotifications { get; } = new();
    public Loc Localization => Loc.Instance;

    // Track instance names the user has explicitly dismissed; suppress that
    // exact instance for ResuppressionWindow before showing it again.
    private readonly Dictionary<string, DateTimeOffset> _dismissedAt = new();
    private readonly TimeSpan _resuppressionWindow = TimeSpan.FromSeconds(60);
    private const int MaxVisibleNotifications = 3;

    public HostDashboardViewModel()
    {
        _orchestrator = AppShell.Current.Host
            ?? throw new InvalidOperationException("HostDashboardViewModel created without active HostOrchestrator");
        _dispatcher = Dispatcher.CurrentDispatcher;
        IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        // Restore persisted host-mute preference + propagate to orchestrator.
        AutoMuteHostMonitor = AppShell.Current.Settings.Current.HostAutoMuteOnSessionStart;
        _orchestrator.AutoMuteHostMonitor = AutoMuteHostMonitor;

        foreach (LaptopCategory cat in Enum.GetValues<LaptopCategory>())
            _groups[cat] = new ClientGroupViewModel(cat);

        _orchestrator.Sessions.ClientJoined += OnClientJoined;
        _orchestrator.Sessions.ClientLeft += OnClientLeft;
        _orchestrator.Sessions.ClientReconnecting += OnClientReconnecting;
        _orchestrator.Sessions.ClientRejoined += OnClientRejoined;
        _orchestrator.Sessions.SessionStateChanged += OnSessionStateChangedHandler;
        _orchestrator.IdleClientDiscovery.ClientsChanged += OnIdleClientsChanged;
        _orchestrator.Diagnostics.ClientQualityUpdated += OnClientQualityUpdated;
        _orchestrator.Control.ClientStatusReceived += OnClientStatusHandler;
        _orchestrator.Log.EntryAdded += OnLogEntryHandler;

        // VU-meter tick runs at ~30 fps for smooth bar animation.
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
            (_, _) =>
            {
                AudioLevel = _orchestrator.Pipeline.CurrentLevel;
                AudioFlowing = _orchestrator.Pipeline.AudioFlowing;
            },
            _dispatcher);
        timer.Start();
    }

    private void OnSessionStateChangedHandler()
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
            }
        });
    }

    private void OnClientJoined(ConnectedClient c)
    {
        _dispatcher.Invoke(() =>
        {
            var vm = new ClientTileViewModel(c, _orchestrator.Control, _orchestrator.Sessions);
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

    /// <summary>
    /// Client's TCP just dropped — keep the tile visible in a "reconnecting"
    /// state rather than removing it. Gives the teacher a visual cue that
    /// the client hasn't actually left, just hiccupped.
    /// </summary>
    private void OnClientReconnecting(ConnectedClient c) =>
        _dispatcher.Invoke(() => FindVm(c.ClientId)?.UpdateFromModel());

    /// <summary>Rejoin completed — same ClientId, fresh ConnectedClient. Rebind the tile.</summary>
    private void OnClientRejoined(ConnectedClient c) =>
        _dispatcher.Invoke(() => FindVm(c.ClientId)?.Rebind(c));

    /// <summary>
    /// mDNS browser saw the idle-client list change. Reconcile the toast list:
    /// add cards for newly-seen idle clients, remove ones that vanished or
    /// already joined (tile exists), and skip recently-dismissed instances.
    /// </summary>
    private void OnIdleClientsChanged(IReadOnlyList<DiscoveredIdleClient> idle) =>
        _dispatcher.BeginInvoke(() =>
        {
            // Drop expired dismissals so they can re-surface naturally.
            var cutoff = DateTimeOffset.UtcNow - _resuppressionWindow;
            foreach (var key in _dismissedAt.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                _dismissedAt.Remove(key);

            // Build the desired notification set: idle clients that are
            // (a) not already represented as a tile in any client group, and
            // (b) not currently dismissed within the resuppression window.
            //
            // Connected tiles store the RAW hostname ("HP-KB-017") while idle
            // clients advertise the FRIENDLY name ("Raum 017") via the mDNS
            // TXT record. Both have to be reduced to the same canonical form
            // — otherwise an already-connected client keeps re-toasting forever.
            // Use ClassroomLaptopName.Classify(...) parsed Room+DeviceIndex tuple
            // as the comparison key.
            var connectedKeys = ClientGroups
                .SelectMany(g => g.Clients)
                .Select(t => CanonicalKey(t.RawHostname))
                .ToHashSet();

            var desired = idle
                .Where(c => !_dismissedAt.ContainsKey(c.InstanceName))
                .Where(c =>
                {
                    var key = CanonicalKey(c.FriendlyName ?? c.InstanceName);
                    return !connectedKeys.Contains(key);
                })
                .OrderBy(c => c.InstanceName, StringComparer.OrdinalIgnoreCase) // stable ordering
                .Take(MaxVisibleNotifications)
                .ToList();

            // Reconcile: remove ones not in `desired`, add new ones.
            var desiredKeys = desired.Select(c => c.InstanceName).ToHashSet();
            var stale = IdleClientNotifications
                .Where(n => !desiredKeys.Contains(n.InstanceName))
                .ToList();
            foreach (var s in stale) IdleClientNotifications.Remove(s);

            foreach (var c in desired)
            {
                if (IdleClientNotifications.All(n => n.InstanceName != c.InstanceName))
                {
                    IdleClientNotifications.Add(new IdleClientNotificationViewModel(
                        c, _orchestrator, DismissNotification));
                }
            }
        });

    private void DismissNotification(IdleClientNotificationViewModel vm)
    {
        _dismissedAt[vm.InstanceName] = DateTimeOffset.UtcNow;
        IdleClientNotifications.Remove(vm);
    }

    /// <summary>
    /// Maps a raw hostname OR a friendly room label down to a stable
    /// case-insensitive key. Used so a connected tile (raw "HP-KB-017") and an
    /// idle-client mDNS advertisement (friendly "Raum 017") deduplicate against
    /// each other.
    /// <para>
    /// Strategy: try Classify on the raw input first (catches HP-KB-017 etc.).
    /// If that returns Sonstige, also try parsing well-known German/English
    /// friendly-prefix patterns ("Raum 017", "Werkstatt 38", "Room 17") so a
    /// pre-friendly-ified label still matches the raw form.
    /// Pure-digit fallback removed — it caused false-positive collisions
    /// (e.g. DESKTOP-A1B2C3 → "123" collided with "Raum 123").
    /// </para>
    /// </summary>
    internal static string CanonicalKey(string nameOrFriendly)
    {
        if (string.IsNullOrWhiteSpace(nameOrFriendly)) return "";

        var parsed = ClassroomLaptopName.Classify(nameOrFriendly);
        if (parsed.Category != LaptopCategory.Sonstige)
            return CategoryKey(parsed.Category, parsed.Room, parsed.DeviceIndex);

        // Friendly-prefix patterns. "(Gerät 2)" / "(device 2)" optional suffix.
        var trimmed = nameOrFriendly.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(?<prefix>Raum|Room|Werkstatt|Workshop)\s+(?<room>\d{1,4})(?:\s*\((?:Gerät|device)\s*(?<device>\d+)\))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var prefix = m.Groups["prefix"].Value.ToLowerInvariant();
            var category = prefix is "raum" or "room"
                ? LaptopCategory.Klassenraum
                : LaptopCategory.Werkstatt;
            // Pad room to 3 digits so "Raum 17" and "HP-KB-017" both hash to "017".
            var room = m.Groups["room"].Value.PadLeft(3, '0');
            int? device = m.Groups["device"].Success ? int.Parse(m.Groups["device"].Value) : null;
            return CategoryKey(category, room, device);
        }

        // Truly unrecognized: fall back to the trimmed lowercase string itself.
        // No digit-only collapse — that caused collisions across distinct rooms.
        return $"raw|{trimmed.ToLowerInvariant()}";
    }

    private static string CategoryKey(LaptopCategory cat, string room, int? device) =>
        $"{cat}|{room}|{device}";

    private void OnClientQualityUpdated(ConnectedClient c) =>
        _dispatcher.Invoke(() => FindVm(c.ClientId)?.UpdateFromModel());

    private void OnClientStatusHandler(ConnectedClient c, ClientStatus status) =>
        _dispatcher.Invoke(() => FindVm(c.ClientId)?.UpdateFromModel());

    private ClientTileViewModel? FindVm(string clientId)
    {
        foreach (var g in _groups.Values)
        {
            var vm = g.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (vm is not null) return vm;
        }
        return null;
    }

    private void OnLogEntryHandler(LogEntry e) =>
        _dispatcher.BeginInvoke(() =>
        {
            LogEntries.Insert(0, e);
            if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
        });

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
            ControlPort = _orchestrator.Control.Port;  // reflects auto-picked port
            FormattedSessionCode = FormatCodeForDisplay(_orchestrator.Sessions.Code ?? "");
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

    private static string FormatCodeForDisplay(string code) =>
        code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;

    [RelayCommand] private async Task StopSessionAsync() =>
        await _orchestrator.StopSessionAsync();

    /// <summary>
    /// Pre-flight test tone: emits a 10s 440Hz sine through the encode +
    /// broadcast pipeline so the teacher can verify "all clients are
    /// receiving" before launching the actual exam audio.
    /// </summary>
    [RelayCommand]
    private void StartTestTone()
    {
        if (!_orchestrator.Sessions.IsActive) return;
        _orchestrator.TestTone.Start();
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

    partial void OnAutoMuteHostMonitorChanged(bool value)
    {
        _orchestrator.AutoMuteHostMonitor = value;
        AppShell.Current.Settings.Current.HostAutoMuteOnSessionStart = value;
        AppShell.Current.Settings.Save();
        // If a session is already running, apply the change live so the
        // teacher gets immediate feedback from the toggle.
        if (_orchestrator.Sessions.IsActive)
        {
            if (value) _orchestrator.MonitorMute.Mute();
            else _orchestrator.MonitorMute.Restore();
        }
    }

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
        try { System.Diagnostics.Process.Start("explorer.exe", _orchestrator.Log.LogDirectory); }
        catch { }
    }

    [RelayCommand]
    private void BuildDiagnosticsBundle()
    {
        try
        {
            var path = DiagnosticsBundleService.Build("Host");
            _orchestrator.Log.Info("UI", $"Diagnose-Paket erstellt: {path}");
            // Highlight the file in Explorer so the teacher sees where it landed.
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
        }
        catch (Exception ex)
        {
            _orchestrator.Log.Warn("UI", "Diagnose-Paket konnte nicht erstellt werden", ex);
        }
    }

    [RelayCommand] private void ClearLogView() => LogEntries.Clear();

    [RelayCommand]
    private void OpenInviteDialog()
    {
        var dlg = new Views.InviteClientDialog(_orchestrator)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dlg.Show();
    }

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
