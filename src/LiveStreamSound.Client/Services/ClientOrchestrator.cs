using System.Net;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Top-level client facade: owns all services and wires them together.
/// ViewModel talks to this; this handles the streaming pipeline.
///
/// <para>
/// Once <see cref="ConnectAsync"/> succeeds, the orchestrator becomes
/// <see cref="IsSessionActive"/> = true and will automatically re-establish
/// the control channel if it drops (WLAN hiccup, host reboot, etc.) using
/// exponential backoff. The session ends only when the user clicks
/// Disconnect, gets kicked, or the host announces SessionEnding.
/// </para>
/// </summary>
public sealed class ClientOrchestrator : IAsyncDisposable
{
    public LogService Log { get; }
    public MDnsDiscoveryService Discovery { get; }
    public ControlClient Control { get; }
    public ClockSyncService ClockSync { get; }
    public OpusDecoderService Decoder { get; }
    public SyncBuffer Buffer { get; }
    public AudioStreamClient AudioIn { get; }
    public AudioPlaybackService Playback { get; }
    public ClientDiagnosticsService Diagnostics { get; }
    public IdleListenerService IdleListener { get; }

    public string SuggestedDisplayName { get; }
    public string HostName { get; } = Environment.MachineName;

    // --- Sticky session state for self-healing reconnects ---
    private IPAddress? _lastHost;
    private int _lastPort;
    private string? _lastCode;
    private string? _lastDisplayName;
    private bool _sessionActive;
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private readonly object _reconnectLock = new();

    /// <summary>True while the orchestrator intends to stay connected (even if the control
    /// channel is momentarily down). Only flipped off by user-initiated disconnect,
    /// kick, or terminal auth-fail.</summary>
    public bool IsSessionActive => _sessionActive;

    /// <summary>Current reconnect attempt count (0 = first connect / healthy).</summary>
    public int ReconnectAttempt => _reconnectAttempt;

    /// <summary>Raised whenever <see cref="IsSessionActive"/> or <see cref="ReconnectAttempt"/> change.
    /// Use for UI bindings.</summary>
    public event Action? ReconnectStatusChanged;

    private Timer? _pingTimer;
    private Timer? _drainTimer;
    private Timer? _statusTimer;

    public ClientOrchestrator()
    {
        Log = new LogService();
        Discovery = new MDnsDiscoveryService(Log);
        Control = new ControlClient(Log);
        ClockSync = new ClockSyncService();
        Decoder = new OpusDecoderService();
        Buffer = new SyncBuffer(ClockSync);
        AudioIn = new AudioStreamClient(Decoder, Buffer, Log);
        Playback = new AudioPlaybackService(Log);
        Diagnostics = new ClientDiagnosticsService(Control, AudioIn, Buffer, ClockSync);
        IdleListener = new IdleListenerService(Log);

        SuggestedDisplayName = ClassroomLaptopName.FriendlyName(HostName);

        Control.MessageReceived += OnMessage;
        Control.StateChanged += OnStateChanged;
    }

    /// <summary>Start listening for host invitations on TCP 5002 and advertising via mDNS.</summary>
    public void StartIdleListener() => IdleListener.Start(SuggestedDisplayName);

    public Task StopIdleListenerAsync() => IdleListener.StopAsync();

    public async Task<bool> ConnectAsync(IPAddress host, int controlPort, string code, string displayName, CancellationToken ct = default)
    {
        // NOTE: We deliberately do NOT stop IdleListener here — doing so races with
        // the in-flight InvitationResponse write (host sees `null` on the invite
        // socket). Instead the state-change handler below stops the listener when
        // the TCP control channel actually reaches Connected.

        var welcome = await Control.ConnectAsync(host, controlPort, code, displayName, ct);
        if (welcome is null) return false;

        try
        {
            await StartAudioPipelineAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Orchestrator", "Audio setup failed after handshake — disconnecting", ex);
            try { await AudioIn.Stop(); } catch { }
            try { Playback.Stop(); } catch { }
            try { await Control.DisposeAsync(); } catch { }
            throw new ClientConnectException(ClassifyConnectError(ex), ex);
        }

        // Remember the connection parameters so the reconnect loop can restore them.
        _lastHost = host;
        _lastPort = controlPort;
        _lastCode = code;
        _lastDisplayName = displayName;
        _sessionActive = true;
        _reconnectAttempt = 0;
        ReconnectStatusChanged?.Invoke();

        StartPumps();
        return true;
    }

    private async Task StartAudioPipelineAsync(CancellationToken ct)
    {
        // Ephemeral UDP port — lets host+client coexist on one machine.
        await AudioIn.StartAsync(port: 0);
        Playback.Start();
        // Tell the host our actual bound port so it fans-out audio to us.
        await Control.SendAsync(new AudioClientReady(AudioIn.Port), ct);
    }

    private void StartPumps()
    {
        StopPumps(); // idempotent
        // First ping immediately so clock-sync converges fast; status reports stagger a bit.
        _pingTimer = new Timer(_ => SendPing(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        _drainTimer = new Timer(_ => DrainToPlayback(), null, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        _statusTimer = new Timer(_ => SendStatus(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// User-initiated disconnect: cancels any pending reconnect loop, tears
    /// the control channel down, and flips the session inactive so no
    /// auto-reconnect kicks in afterwards.
    /// </summary>
    public async Task DisconnectByUserAsync()
    {
        _sessionActive = false;
        ReconnectStatusChanged?.Invoke();
        CancelReconnectLoop();
        StopPumps();
        try { await AudioIn.Stop(); } catch { }
        try { Playback.Stop(); } catch { }
        await Control.DisposeAsync();
        // Let Discovery/IdleListener restart via OnStateChanged.
    }

    private static string ClassifyConnectError(Exception ex)
    {
        if (ex is System.Net.Sockets.SocketException sx &&
            sx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            return "audio-port-blocked";
        if (ex.Message.Contains("device", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
            return "no-audio-device";
        return "unknown";
    }

    private void OnStateChanged(ControlClientState s)
    {
        if (s == ControlClientState.Connected)
        {
            // Once we have an active session, stop looking for *new* connections:
            // mDNS browse for other hosts AND the invite listener both go dark until
            // we disconnect. By this point the InvitationResponse has already
            // flushed out on the invite socket.
            _ = IdleListener.StopAsync();
            Discovery.Stop();
        }
        else if (s == ControlClientState.Disconnected || s == ControlClientState.Failed)
        {
            StopPumps();
            _ = AudioIn.Stop();
            Playback.Stop();

            if (_sessionActive && _lastHost is not null)
            {
                // Drop back into reconnect loop — don't re-enable discovery/invite
                // until the user explicitly disconnects.
                StartReconnectLoop();
            }
            else
            {
                // Pure idle — resume looking for new connections.
                Discovery.Start();
                IdleListener.Start(SuggestedDisplayName);
            }
        }
    }

    private void StartReconnectLoop()
    {
        lock (_reconnectLock)
        {
            if (_reconnectTask is not null && !_reconnectTask.IsCompleted) return;
            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(ct));
        }
    }

    private void CancelReconnectLoop()
    {
        lock (_reconnectLock)
        {
            try { _reconnectCts?.Cancel(); } catch { }
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        Control.MarkReconnecting();
        var host = _lastHost!;
        var port = _lastPort;
        var code = _lastCode!;
        var name = _lastDisplayName!;

        while (!ct.IsCancellationRequested && _sessionActive)
        {
            _reconnectAttempt++;
            ReconnectStatusChanged?.Invoke();

            // Exponential backoff w/ jitter: 500, 1000, 2000, 4000, 8000, cap 15000.
            var baseDelay = Math.Min(500 * (int)Math.Pow(2, _reconnectAttempt - 1), 15_000);
            var jitter = Random.Shared.Next(0, 400);
            var delay = baseDelay + jitter;
            Log.Info("Orchestrator",
                $"Reconnect attempt #{_reconnectAttempt} in {delay}ms → {host}:{port}");
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                var welcome = await Control.ConnectAsync(host, port, code, name, ct).ConfigureAwait(false);
                if (welcome is null)
                {
                    // Might be AUTH_FAIL — check state. If Failed + error was auth,
                    // give up (host is running a different session code now).
                    if (Control.State == ControlClientState.Failed)
                    {
                        Log.Warn("Orchestrator",
                            $"Reconnect gave up: auth failed (host probably restarted session with a different code)");
                        _sessionActive = false;
                        ReconnectStatusChanged?.Invoke();
                        return;
                    }
                    continue; // transient failure, backoff and retry
                }

                await StartAudioPipelineAsync(ct).ConfigureAwait(false);
                StartPumps();
                _reconnectAttempt = 0;
                ReconnectStatusChanged?.Invoke();
                Log.Info("Orchestrator", $"Reconnected to {host}:{port}");
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warn("Orchestrator", $"Reconnect attempt #{_reconnectAttempt} failed: {ex.Message}");
            }
        }
    }

    private void OnMessage(ControlMessage msg)
    {
        switch (msg)
        {
            case Pong pong:
                ClockSync.NotifyPong(pong.ClientTimeMs, pong.ServerTimeMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                break;
            case SetVolume sv:
                Playback.Volume = sv.Level;
                break;
            case SetMute sm:
                Playback.IsMuted = sm.Muted;
                break;
            case ListOutputDevicesRequest:
                SendOutputDeviceList();
                break;
            case SetOutputDevice sd:
                try { Playback.SwitchDevice(sd.DeviceId); }
                catch (Exception ex) { Log.Warn("Orchestrator", $"Switch device failed: {ex.Message}"); }
                break;
            case Kick kick:
                Log.Info("Orchestrator", $"Kicked by host: {kick.Reason}");
                // Terminal — don't auto-reconnect after a kick.
                _sessionActive = false;
                ReconnectStatusChanged?.Invoke();
                _ = Control.DisposeAsync();
                break;
            case SessionEnding se:
                Log.Info("Orchestrator", $"Session ending: {se.Reason}");
                // Terminal — host is explicitly ending the session.
                _sessionActive = false;
                ReconnectStatusChanged?.Invoke();
                _ = Control.DisposeAsync();
                break;
        }
    }

    private void SendPing()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _ = Control.SendAsync(new Ping(now));
    }

    private void DrainToPlayback()
    {
        foreach (var pcm in Buffer.DrainReady())
            Playback.WritePcm(pcm);
    }

    private void SendStatus()
    {
        var status = new ClientStatus(
            CurrentVolume: Playback.Volume,
            IsMuted: Playback.IsMuted,
            CurrentDeviceId: Playback.CurrentDeviceId,
            BufferedMs: Buffer.CurrentBufferedMs);
        _ = Control.SendAsync(status);
    }

    private void SendOutputDeviceList()
    {
        var devices = AudioPlaybackService.EnumerateDevices();
        var proto = devices.Select(d => new OutputDeviceInfo(d.Id, d.Name, d.IsDefault)).ToArray();
        _ = Control.SendAsync(new OutputDevicesResponse(proto, Playback.CurrentDeviceId));
    }

    private void StopPumps()
    {
        _pingTimer?.Dispose();
        _drainTimer?.Dispose();
        _statusTimer?.Dispose();
        _pingTimer = null;
        _drainTimer = null;
        _statusTimer = null;
    }

    public async ValueTask DisposeAsync()
    {
        _sessionActive = false;
        CancelReconnectLoop();
        if (_reconnectTask is not null)
        {
            try { await _reconnectTask.ConfigureAwait(false); } catch { }
        }
        StopPumps();
        await IdleListener.DisposeAsync();
        await Control.DisposeAsync();
        await AudioIn.DisposeAsync();
        Playback.Dispose();
        Discovery.Dispose();
        Diagnostics.Dispose();
        Log.Dispose();
    }
}
