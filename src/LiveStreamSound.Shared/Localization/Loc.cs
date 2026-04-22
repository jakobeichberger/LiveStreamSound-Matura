using System.ComponentModel;
using System.Globalization;

namespace LiveStreamSound.Shared.Localization;

/// <summary>
/// Lightweight runtime localization. XAML binds to <c>{Binding [Key], Source={x:Static loc:Loc.Instance}}</c>
/// and all bindings re-evaluate automatically when <see cref="Language"/> is changed.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    public enum Lang { German, English }

    private Lang _language = DetectInitialLanguage();
    public Lang Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            CultureInfo.CurrentUICulture = value == Lang.German
                ? CultureInfo.GetCultureInfo("de-DE")
                : CultureInfo.GetCultureInfo("en-US");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGerman)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
        }
    }

    public bool IsGerman => _language == Lang.German;
    public bool IsEnglish => _language == Lang.English;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_strings.TryGetValue(key, out var pair))
            return _language == Lang.German ? pair.De : pair.En;
        return key;
    }

    public void Toggle() => Language = _language == Lang.German ? Lang.English : Lang.German;

    private static Lang DetectInitialLanguage()
    {
        try
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return two == "de" ? Lang.German : Lang.English;
        }
        catch
        {
            return Lang.German;
        }
    }

    private readonly record struct Pair(string De, string En);

    private readonly Dictionary<string, Pair> _strings = new()
    {
        // Common
        ["AppName.Host"] = new("LiveStreamSound — Host", "LiveStreamSound — Host"),
        ["AppName.Client"] = new("LiveStreamSound — Client", "LiveStreamSound — Client"),
        ["Language"] = new("Sprache", "Language"),
        ["LanguageGerman"] = new("Deutsch", "German"),
        ["LanguageEnglish"] = new("Englisch", "English"),
        ["Theme"] = new("Design", "Theme"),
        ["ThemeLight"] = new("Hell", "Light"),
        ["ThemeDark"] = new("Dunkel", "Dark"),
        ["ThemeSystem"] = new("System", "System"),
        ["Help"] = new("Hilfe", "Help"),
        ["CloseHelp"] = new("Schließen", "Close"),
        ["Log"] = new("Fehlerlog", "Error log"),
        ["LogOpenFolder"] = new("Log-Ordner öffnen", "Open log folder"),
        ["LogClear"] = new("Anzeige leeren", "Clear view"),
        ["Ok"] = new("OK", "OK"),
        ["Cancel"] = new("Abbrechen", "Cancel"),
        ["Connect"] = new("Verbinden", "Connect"),
        ["Disconnect"] = new("Trennen", "Disconnect"),
        ["Settings"] = new("Einstellungen", "Settings"),

        // Host
        ["Host.StartSession"] = new("Sitzung starten", "Start session"),
        ["Host.StopSession"] = new("Sitzung beenden", "Stop session"),
        ["Host.SessionCode"] = new("Sitzungscode", "Session code"),
        ["Host.ShowQr"] = new("QR-Code anzeigen", "Show QR code"),
        ["Host.ConnectedClients"] = new("Verbundene Clients", "Connected clients"),
        ["Host.NoClients"] = new("Noch keine Clients verbunden.", "No clients connected yet."),
        ["Host.Volume"] = new("Lautstärke", "Volume"),
        ["Host.Mute"] = new("Stumm", "Mute"),
        ["Host.MuteOn"] = new("Stumm (tippen zum Aufheben)", "Muted (tap to unmute)"),
        ["Host.MuteOff"] = new("Ton an (tippen zum Stummschalten)", "Sound on (tap to mute)"),
        ["Host.OutputDevice"] = new("Ausgabegerät", "Output device"),
        ["Host.Kick"] = new("Client trennen", "Kick client"),
        ["Host.CapturingAudio"] = new("Audio-Aufnahme aktiv", "Audio capture active"),
        ["Host.NoAudio"] = new("Keine Audio-Quelle aktiv", "No audio source active"),
        ["Host.HostIp"] = new("Host-IP", "Host IP"),
        ["Host.Port"] = new("Port", "Port"),
        ["Host.Status.Idle"] = new("Keine Sitzung aktiv. Klicke auf »Sitzung starten«.", "No active session. Click \"Start session\"."),
        ["Host.Status.Running"] = new("Sitzung läuft — Clients können jetzt beitreten.", "Session running — clients may now join."),
        ["Host.GroupClassrooms"] = new("Klassenräume", "Classrooms"),
        ["Host.GroupWorkshop"] = new("Werkstatt", "Workshop"),
        ["Host.GroupRooms"] = new("Räume", "Rooms"),
        ["Host.GroupOther"] = new("Sonstige Geräte", "Other devices"),
        ["Host.AutoMuteToggle"] = new(
            "Host stumm während Session",
            "Mute host during session"),
        ["Host.AutoMuteTooltip"] = new(
            "Schaltet die Lautsprecher des Lehrer-Laptops stumm, sobald die Session startet — Clients hören den Stream weiter. Beim Beenden wird der vorige Zustand wiederhergestellt.",
            "Mutes the teacher laptop's own speakers when a session starts — clients still hear the stream. The previous state is restored on stop."),

        // Session start errors (host side)
        ["Host.Error.PortInUse.Title"] = new("Port belegt", "Port in use"),
        ["Host.Error.PortInUse.Body"] = new(
            "Die Ports 5000 (Steuerung) oder 5001 (Audio) werden bereits verwendet. Läuft eventuell noch eine andere LiveStreamSound-Instanz oder ein anderes Programm auf diesen Ports? Laptop neu starten oder das andere Programm schließen.",
            "Ports 5000 (control) or 5001 (audio) are already in use. Is another LiveStreamSound instance or another app already running on these ports? Close it or restart the laptop."),
        ["Host.Error.NoAudioDevice.Title"] = new("Kein Audio-Gerät", "No audio device"),
        ["Host.Error.NoAudioDevice.Body"] = new(
            "Es ist kein Standard-Audio-Ausgabegerät auf dem Laptop aktiv. Ein Ausgabegerät in den Windows-Sound-Einstellungen als Standard setzen.",
            "No default audio output device is active. Set a default playback device in Windows sound settings."),
        ["Host.Error.SocketError.Title"] = new("Netzwerk-Problem", "Network error"),
        ["Host.Error.SocketError.Body"] = new(
            "Netzwerk-Socket konnte nicht geöffnet werden. Ist der Laptop mit einem Netzwerk verbunden (WLAN oder LAN)?",
            "Could not open network socket. Is the laptop connected to a network (Wi-Fi or LAN)?"),
        ["Host.Error.Unknown.Title"] = new("Sitzung konnte nicht gestartet werden", "Could not start session"),
        ["Host.Error.Unknown.Body"] = new(
            "Ein unerwarteter Fehler ist aufgetreten. Details im Fehlerlog (Icon oben rechts).",
            "An unexpected error occurred. Details in the error log (top right)."),

        // Client connect errors
        ["Client.Error.Connecting"] = new("Verbinde…", "Connecting…"),
        ["Client.Error.NoAudioDevice.Title"] = new("Kein Audio-Ausgabegerät", "No audio output device"),
        ["Client.Error.NoAudioDevice.Body"] = new(
            "Es wurde kein Audio-Ausgabegerät gefunden. HDMI-Kabel prüfen oder in den Windows-Sound-Einstellungen ein Gerät aktivieren.",
            "No audio output device found. Check the HDMI cable or enable a device in Windows sound settings."),
        ["Client.Error.AudioPortBlocked.Title"] = new("Audio-Port blockiert", "Audio port blocked"),
        ["Client.Error.AudioPortBlocked.Body"] = new(
            "Der UDP-Audio-Port (5001) kann nicht geöffnet werden — wahrscheinlich wird er von einem anderen Programm verwendet oder die Firewall blockiert ihn.",
            "The UDP audio port (5001) cannot be opened — likely another app uses it or the firewall is blocking."),

        // Client: connected state
        ["Client.Connected.Title"] = new("Verbunden", "Connected"),
        ["Client.Connected.Subtitle"] = new("Ton wird wiedergegeben, sobald der Host etwas abspielt.", "Audio plays as soon as the host starts something."),
        ["Client.Connected.Host"] = new("Host", "Host"),
        ["Client.Connected.ChangeDevice"] = new("Ausgabegerät wechseln", "Change output device"),

        // Client: discovered hosts list
        ["Client.PickHost"] = new("Einen Host auswählen oder IP eingeben", "Pick a host or enter an IP"),
        ["Client.NoHostsFound"] = new("Keine Hosts automatisch gefunden. Bitte IP manuell eintragen (siehe unten).", "No hosts found automatically. Please enter the IP manually below."),

        // Accessibility names for icon-only buttons and meaningful graphics
        ["A11y.LanguageToggle"] = new("Sprache wechseln", "Change language"),
        ["A11y.ThemeToggle"] = new("Design wechseln (hell/dunkel)", "Change theme (light/dark)"),
        ["A11y.HelpButton"] = new("Hilfe öffnen oder schließen", "Open or close help"),
        ["A11y.LogButton"] = new("Fehlerlog öffnen oder schließen", "Open or close error log"),
        ["A11y.ClosePanel"] = new("Schließen", "Close"),
        ["A11y.KickClient"] = new("Diesen Client trennen", "Disconnect this client"),
        ["A11y.QrCode"] = new("QR-Code mit Verbindungs-Link zum Sitzungs-Host", "QR code containing session host link"),
        ["A11y.QualityGood"] = new("Verbindung gut", "Connection good"),
        ["A11y.QualityDegraded"] = new("Verbindung eingeschränkt", "Connection degraded"),
        ["A11y.QualityBad"] = new("Verbindung gestört", "Connection poor"),
        ["A11y.QualityDisconnected"] = new("Verbindung getrennt", "Disconnected"),
        ["A11y.VolumeSlider"] = new("Lautstärke in Prozent", "Volume in percent"),
        ["A11y.SessionCode"] = new("Sechsstelliger Sitzungscode", "Six-digit session code"),

        // Role selection (start screen)
        ["App.Welcome"] = new("Willkommen", "Welcome"),
        ["App.ChooseRole"] = new("Was möchtest du tun?", "What would you like to do?"),
        ["App.Role.SendTitle"] = new("Ton senden", "Send audio"),
        ["App.Role.SendSubtitle"] = new(
            "Dein Laptop ist der Host. Andere Geräte hören den Ton, den du hier abspielst (z.B. in VLC).",
            "Your laptop is the host. Other devices play the audio you play here (e.g. in VLC)."),
        ["App.Role.ReceiveTitle"] = new("Ton empfangen", "Receive audio"),
        ["App.Role.ReceiveSubtitle"] = new(
            "Dein Laptop empfängt den Ton von einem anderen Laptop und gibt ihn hier aus.",
            "Your laptop receives audio from another laptop and plays it here."),
        ["App.SwitchRole"] = new("Rolle wechseln…", "Change role…"),
        ["App.SwitchRoleConfirmTitle"] = new("Rolle wechseln?", "Change role?"),
        ["App.SwitchRoleConfirmBody"] = new(
            "Die aktive Sitzung wird dabei getrennt.",
            "Your active session will be disconnected."),
        ["App.Continue"] = new("Fortfahren", "Continue"),

        // Invite flow (Host → idle Client)
        ["Invite.DialogTitle"] = new("Client einladen", "Invite client"),
        ["Invite.IdleClients"] = new("Wartende Clients im Netzwerk", "Idle clients on the network"),
        ["Invite.NoIdleClients"] = new("Aktuell kein wartender Client gefunden.", "No idle clients found right now."),
        ["Invite.ManualTarget"] = new("Oder direkt per IP", "Or directly by IP"),
        ["Invite.InvitationButton"] = new("Einladen", "Invite"),
        ["Invite.Cancel"] = new("Abbrechen", "Cancel"),
        ["Invite.Sending"] = new("Einladung wird gesendet…", "Sending invitation…"),
        ["Invite.Error.Unreachable"] = new("Der Client ist nicht erreichbar. IP, Port und WLAN prüfen.", "The client is not reachable. Check IP, port and Wi-Fi."),
        ["Invite.Error.Rejected"] = new("Der Client hat abgelehnt.", "The client declined the invitation."),
        ["Invite.OpenInviteDialog"] = new("Client hinzufügen", "Add client"),

        // Incoming invite (on the Client side)
        ["Incoming.Title"] = new("📥 Einladung erhalten", "📥 You're invited"),
        ["Incoming.Body"] = new("{0} (IP {1}) lädt dich in Session {2} ein.", "{0} (IP {1}) is inviting you to session {2}."),
        ["Incoming.Accept"] = new("Annehmen", "Accept"),
        ["Incoming.Reject"] = new("Ablehnen", "Decline"),

        // Idle-client toast notifications on the Host dashboard
        ["Notification.IdleClient.Subtitle"] = new(
            "Wartet auf Einladung",
            "Waiting for invitation"),
        ["Notification.IdleClient.AddTooltip"] = new(
            "Diesen Raum mit einem Klick zur Sitzung hinzufügen",
            "Add this room to the session in one click"),
        ["Notification.IdleClient.DismissTooltip"] = new(
            "Benachrichtigung ausblenden",
            "Dismiss notification"),

        // Client
        ["Client.DiscoveredHosts"] = new("Gefundene Hosts", "Discovered hosts"),
        ["Client.ManualEntry"] = new("Manuell eingeben", "Manual entry"),
        ["Client.Code"] = new("Sitzungscode", "Session code"),
        ["Client.DisplayName"] = new("Angezeigter Name", "Display name"),
        ["Client.Connected"] = new("Verbunden", "Connected"),
        ["Client.Connecting"] = new("Verbinde…", "Connecting…"),
        ["Client.Disconnected"] = new("Nicht verbunden", "Disconnected"),
        ["Client.OutputDevice"] = new("Audio-Ausgabe", "Audio output"),
        ["Client.Buffered"] = new("Puffer (ms)", "Buffer (ms)"),
        ["Client.Rtt"] = new("Latenz (ms)", "Latency (ms)"),
        ["Client.PacketLoss"] = new("Paketverlust (%)", "Packet loss (%)"),
        ["Client.Rooms.Suggested"] = new("Vorschlag", "Suggested"),

        // Simple "Lehrer-Modus" hero copy
        ["Client.Plain.AllGood"] = new("Alles läuft", "All set"),
        ["Client.Plain.AllGoodBody"] = new(
            "Du hörst den Ton vom Host-Laptop, sobald dort etwas abgespielt wird.",
            "You'll hear audio as soon as the host plays something."),
        ["Client.Plain.Degraded"] = new("Verbunden — Qualität schwankt", "Connected — quality fluctuating"),
        ["Client.Plain.Bad"] = new("Verbunden, aber Probleme", "Connected, but with issues"),
        ["Client.Plain.Reconnecting"] = new("Ich stelle die Verbindung wieder her…", "Restoring the connection…"),
        ["Client.Plain.NotConnected"] = new("Noch nicht verbunden", "Not connected yet"),
        ["Client.View.Simple"] = new("Lehrer-Ansicht", "Teacher view"),
        ["Client.View.Technician"] = new("Techniker-Ansicht", "Technician view"),
        ["Client.View.ToggleTooltip"] = new(
            "Zwischen einfacher Ansicht (Lehrer) und detaillierter Ansicht (Techniker) wechseln",
            "Switch between simple (teacher) and detailed (technician) view"),

        // Self-healing reconnect UX
        ["Client.Reconnecting.Title"] = new("Verbindung wird wiederhergestellt…", "Reconnecting…"),
        ["Client.Reconnecting.Body"] = new(
            "Die Verbindung zum Host wurde unterbrochen — ich versuche sie automatisch wieder aufzunehmen. Klick auf »Trennen«, um abzubrechen.",
            "The connection to the host dropped — I'll reconnect automatically. Click \"Disconnect\" to cancel."),
        ["Client.Reconnecting.Attempt"] = new("Versuch {0}", "Attempt {0}"),
        ["Client.Reconnecting.Hint"] = new(
            "Einstellungen (Lautstärke, Ausgabegerät) bleiben erhalten.",
            "Your settings (volume, output device) are preserved."),
        ["Host.ClientReconnecting"] = new("Verbindet neu…", "Reconnecting…"),
        ["Host.ClientReconnectingHint"] = new(
            "Client hat die Verbindung verloren. Ich behalte den Platz {0} Sekunden lang frei.",
            "Client lost its connection. I'll hold the slot for {0} seconds."),

        // Quality badges
        ["Quality.Good"] = new("Gut", "Good"),
        ["Quality.Degraded"] = new("Einschränkungen", "Degraded"),
        ["Quality.Bad"] = new("Gestört", "Poor"),
        ["Quality.Disconnected"] = new("Getrennt", "Disconnected"),

        // Connection issues — user-visible descriptions
        ["Issue.None"] = new("Alles in Ordnung.", "All good."),
        ["Issue.NoAudioStreamOnHost.Title"] = new("Kein Audio auf dem Host", "No audio on host"),
        ["Issue.NoAudioStreamOnHost.Body"] = new(
            "Der Host nimmt gerade nichts auf. Starte die Wiedergabe in VLC, im Browser oder einem anderen Player auf dem Host-Laptop.",
            "The host is not capturing any audio. Start playback in VLC, the browser, or another player on the host laptop."),
        ["Issue.HighLatency.Title"] = new("Hohe Netzwerk-Latenz", "High network latency"),
        ["Issue.HighLatency.Body"] = new(
            "Die Antwortzeit ist höher als erwartet. Prüfe das WLAN-Signal und ob das Netz stark ausgelastet ist.",
            "Round-trip time is higher than expected. Check the Wi-Fi signal and whether the network is congested."),
        ["Issue.PacketLoss.Title"] = new("Audio-Pakete gehen verloren", "Audio packets are being lost"),
        ["Issue.PacketLoss.Body"] = new(
            "Es gehen Audio-Pakete verloren. Das verursacht kurze Aussetzer. Meist hilft: Host und Clients näher an den WLAN-AP bringen oder per LAN verbinden.",
            "Audio packets are being dropped — this causes small glitches. Usually: move closer to the Wi-Fi AP or switch to wired Ethernet."),
        ["Issue.BufferUnderrun.Title"] = new("Puffer zu niedrig", "Buffer running low"),
        ["Issue.BufferUnderrun.Body"] = new(
            "Audio-Daten kommen zu langsam an. Netzwerk-Bandbreite prüfen oder Puffer-Latenz in den Einstellungen erhöhen.",
            "Audio frames arrive too slowly. Check bandwidth or raise the jitter buffer in settings."),
        ["Issue.ClockDriftHigh.Title"] = new("Uhren weichen ab", "Clock drift"),
        ["Issue.ClockDriftHigh.Body"] = new(
            "Host- und Client-Uhren haben sich auseinanderbewegt. Eine kurze Resynchronisation ist möglich.",
            "Host and client clocks have drifted apart. A short resync may occur."),
        ["Issue.Disconnected.Title"] = new("Verbindung unterbrochen", "Connection lost"),
        ["Issue.Disconnected.Body"] = new(
            "Die Steuerverbindung zum Host ist weg. Das kann am WLAN liegen oder daran, dass der Host die Sitzung beendet hat. »Neu verbinden« versuchen.",
            "Control connection to host has been lost. Could be Wi-Fi or the host ended the session. Try reconnecting."),
        ["Issue.NetworkIsolation.Title"] = new("WLAN isoliert Clients", "Wi-Fi isolating clients"),
        ["Issue.NetworkIsolation.Body"] = new(
            "Das WLAN trennt Geräte voneinander — die automatische Suche findet den Host nicht. Host-IP manuell eingeben, oder IT fragen, ob die Client-Isolation im Schul-WLAN abgeschaltet werden kann.",
            "The Wi-Fi AP isolates clients — automatic discovery cannot see the host. Enter the host IP manually, or ask IT to disable client isolation."),
        ["Issue.FirewallUdpBlocked.Title"] = new("Firewall blockt den Audio-Stream", "Firewall blocks the audio stream"),
        ["Issue.FirewallUdpBlocked.Body"] = new(
            "Die Steuerverbindung läuft, aber es kommen keine Audio-Pakete an. Sehr wahrscheinlich blockt die Windows-Firewall den UDP-Port. Die App nach dem ersten Start als vertrauenswürdig markieren.",
            "The control channel is up but no audio is arriving. Most likely the Windows Firewall is blocking the UDP audio port. Allow the app through the firewall."),
        ["Issue.WlanSignalWeak.Title"] = new("Schwaches WLAN-Signal", "Weak Wi-Fi signal"),
        ["Issue.WlanSignalWeak.Body"] = new(
            "Das WLAN-Signal ist schwach. Näher an den AP oder LAN-Kabel verwenden.",
            "Wi-Fi signal is weak. Move closer to the AP or use a LAN cable."),
        ["Issue.ClientNotResponding.Title"] = new("Client antwortet nicht", "Client not responding"),
        ["Issue.ClientNotResponding.Body"] = new(
            "Der Client meldet seit mehreren Sekunden keinen Status. Möglicherweise ist er eingeschlafen oder nicht mehr im Netz.",
            "The client hasn't reported status for several seconds. It may be asleep or off the network."),

        // Help page — Host
        ["HelpHost.Title"] = new("Bedienungshilfe — Host", "How to use — Host"),
        ["HelpHost.Step1"] = new(
            "1. Klicke auf »Sitzung starten«. Der Code und die Host-IP werden angezeigt.",
            "1. Click \"Start session\". The code and host IP will appear."),
        ["HelpHost.Step2"] = new(
            "2. Gib Sitzungscode und IP an die Clients weiter (oder zeige den QR-Code).",
            "2. Share the session code and IP with the clients (or show the QR code)."),
        ["HelpHost.Step3"] = new(
            "3. Wenn die Clients verbunden sind: Starte VLC oder den Browser mit der Audio-Datei. Der Ton wird automatisch an alle Clients gestreamt.",
            "3. Once clients are connected, start VLC or the browser with the audio file. Sound will be streamed to every client."),
        ["HelpHost.Step4"] = new(
            "4. Pro Client kannst du Lautstärke, Stummschaltung und Ausgabegerät (z.B. HDMI) fernsteuern.",
            "4. You can remote-control volume, mute and output device (e.g. HDMI) per client."),
        ["HelpHost.Step5"] = new(
            "5. Bei Problemen hilft der Fehlerlog und die Anzeige unter jedem Client.",
            "5. If something goes wrong, check the error log and the status line under each client."),

        // Help page — Client
        ["HelpClient.Title"] = new("Bedienungshilfe — Client", "How to use — Client"),
        ["HelpClient.Step1"] = new(
            "1. Wähle den Host aus der automatisch gefundenen Liste, oder trage die IP von Hand ein.",
            "1. Pick the host from the auto-discovered list, or enter the IP manually."),
        ["HelpClient.Step2"] = new(
            "2. Tippe den 6-stelligen Sitzungscode ein, den der Lehrer am Host zeigt.",
            "2. Type the 6-digit session code shown by the teacher on the host."),
        ["HelpClient.Step3"] = new(
            "3. Wähle das richtige Ausgabegerät (z.B. HDMI) und klicke »Verbinden«.",
            "3. Choose the right output device (e.g. HDMI) and click \"Connect\"."),
        ["HelpClient.Step4"] = new(
            "4. Die Statusleiste zeigt live die Verbindungsqualität. Bei Problemen wird erklärt, was wahrscheinlich falsch ist.",
            "4. The status bar shows live connection quality. On problems it explains what is likely wrong."),
    };
}
