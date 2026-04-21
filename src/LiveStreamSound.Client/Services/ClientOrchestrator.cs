using System.Net;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Top-level client facade: owns all services and wires them together.
/// ViewModel talks to this; this handles the streaming pipeline.
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

    public string SuggestedDisplayName { get; }
    public string HostName { get; } = Environment.MachineName;

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

        SuggestedDisplayName = ClassroomLaptopName.FriendlyName(HostName);

        Control.MessageReceived += OnMessage;
        Control.StateChanged += OnStateChanged;
    }

    public async Task<bool> ConnectAsync(IPAddress host, int controlPort, string code, string displayName, CancellationToken ct = default)
    {
        var welcome = await Control.ConnectAsync(host, controlPort, code, displayName, ct);
        if (welcome is null) return false;

        try
        {
            await AudioIn.StartAsync(welcome.AudioUdpPort);
            Playback.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Orchestrator", "Audio setup failed after handshake — disconnecting", ex);
            try { await AudioIn.Stop(); } catch { }
            try { Playback.Stop(); } catch { }
            try { await Control.DisposeAsync(); } catch { }
            throw new ClientConnectException(ClassifyConnectError(ex), ex);
        }

        // First ping immediately so clock-sync converges fast; status reports stagger a bit.
        _pingTimer = new Timer(_ => SendPing(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        _drainTimer = new Timer(_ => DrainToPlayback(), null, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        _statusTimer = new Timer(_ => SendStatus(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        return true;
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
        if (s == ControlClientState.Disconnected || s == ControlClientState.Failed)
        {
            StopPumps();
            _ = AudioIn.Stop();
            Playback.Stop();
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
                _ = Control.DisposeAsync();
                break;
            case SessionEnding se:
                Log.Info("Orchestrator", $"Session ending: {se.Reason}");
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
        StopPumps();
        await Control.DisposeAsync();
        await AudioIn.DisposeAsync();
        Playback.Dispose();
        Discovery.Dispose();
        Diagnostics.Dispose();
        Log.Dispose();
    }
}
