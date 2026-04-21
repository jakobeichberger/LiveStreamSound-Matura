using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiveStreamSound.Shared.Audio;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Ties all host services together. One instance per host app. Starts/stops a streaming session
/// on user request. Wires capture → encoder → UDP fan-out, and keeps the session manager +
/// control server + mDNS advertise + diagnostics in sync.
/// </summary>
public sealed class HostOrchestrator : IAsyncDisposable
{
    public LogService Log { get; }
    public SessionManager Sessions { get; }
    public ControlServer Control { get; }
    public AudioStreamServer AudioServer { get; }
    public AudioCaptureService Capture { get; }
    public OpusEncoderService Encoder { get; }
    public MDnsAdvertiseService MDns { get; }
    public DiagnosticsService Diagnostics { get; }
    public AudioPipelineState Pipeline { get; }
    public IdleClientDiscoveryService IdleClientDiscovery { get; }
    public InviteClientService InviteClient { get; }

    public string? LocalIp { get; private set; }

    public HostOrchestrator()
    {
        Log = new LogService();
        Pipeline = new AudioPipelineState();
        Sessions = new SessionManager(Log);
        Control = new ControlServer(Sessions, Log);
        AudioServer = new AudioStreamServer(Sessions, Log);
        Capture = new AudioCaptureService();
        Encoder = new OpusEncoderService();
        MDns = new MDnsAdvertiseService(Log);
        Diagnostics = new DiagnosticsService(Sessions, Pipeline, Log);
        IdleClientDiscovery = new IdleClientDiscoveryService(Log);
        InviteClient = new InviteClientService(Log);

        Capture.FrameAvailable += OnPcmFrame;
        Capture.CaptureError += ex => Log.Error("Capture", "Recording error", ex);
        // NOTE: the client sends AudioClientReady over TCP with its actual bound
        // UDP port after HELLO. That message updates client.AudioEndpoint with
        // the right endpoint. We no longer assume DefaultAudioPort here so a
        // host and client can run on the same machine without colliding on 5001.
        Control.ClientStatusReceived += (c, _) => { /* placeholder subscription */ };
    }

    public string StartSession(string? sessionName = null)
    {
        if (Sessions.IsActive) return Sessions.Code!;

        LocalIp = DetectLocalIp();
        var code = Sessions.StartSession();
        var started = 0;
        try
        {
            Control.Start(DiscoveryConstants.DefaultControlPort);  // may auto-pick higher
            started++;
            AudioServer.Start(DiscoveryConstants.DefaultAudioPort);  // may auto-pick higher
            started++;
            // Wire the actual audio port into ControlServer so WELCOME is correct.
            Control.AudioPort = AudioServer.Port;
            MDns.Advertise(
                instanceName: $"LiveStreamSound-{Environment.MachineName}",
                controlPort: Control.Port,
                sessionName: sessionName ?? Environment.MachineName);
            Capture.Start();
            started++;
            Log.Info("Orchestrator", $"Session ready. Clients: {LocalIp}:{Control.Port}, audio {AudioServer.Port}, code={code}");
            return code;
        }
        catch (Exception ex)
        {
            Log.Error("Orchestrator", "Session start failed — rolling back", ex);
            try { Capture.Stop(); } catch { }
            try { MDns.Dispose(); } catch { }
            try { AudioServer.Dispose(); } catch { }
            try { Control.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            Sessions.StopSession();
            throw new SessionStartException(ClassifyStartError(ex), ex);
        }
    }

    private static string ClassifyStartError(Exception ex)
    {
        if (ex is System.Net.Sockets.SocketException sx)
        {
            if (sx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
                return "port-in-use";
            return "socket-error";
        }
        if (ex.Message.Contains("device", StringComparison.OrdinalIgnoreCase))
            return "no-audio-device";
        return "unknown";
    }

    public async Task StopSessionAsync()
    {
        Capture.Stop();
        MDns.Dispose();
        await Control.DisposeAsync();
        AudioServer.Dispose();
        Sessions.StopSession();
        Log.Info("Orchestrator", "Session stopped");
    }

    // Max Opus packet size per RFC 7845 is 1275 bytes; allocate per frame (50×/s) and let GC
    // reclaim — ArrayPool would save bytes but timing is dominated by I/O not GC at this rate.
    private void OnPcmFrame(byte[] pcm)
    {
        Pipeline.NotifyFrame(pcm);
        try
        {
            var payload = new byte[1275];
            var encodedLen = Encoder.Encode(pcm, payload);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _ = AudioServer.BroadcastFrameAsync(AudioPayloadType.Opus,
                new ReadOnlyMemory<byte>(payload, 0, encodedLen), ts);
        }
        catch (Exception ex)
        {
            Log.Error("Encoder", "Opus encode failed", ex);
        }
    }

    private static string DetectLocalIp()
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
            return "127.0.0.1";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopSessionAsync();
        IdleClientDiscovery.Dispose();
        Capture.Dispose();
        Diagnostics.Dispose();
        Log.Dispose();
    }
}
