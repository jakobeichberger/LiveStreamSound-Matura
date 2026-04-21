using System.IO;
using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Audio;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// TCP control server: handles HELLO/WELCOME handshake and per-client command stream.
/// Spawns one task per accepted connection.
/// </summary>
public sealed class ControlServer : IAsyncDisposable
{
    private readonly SessionManager _sessions;
    private readonly LogService _log;
    private readonly AuthAttemptTracker _auth = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    public int Port { get; private set; }

    /// <summary>Set by <see cref="HostOrchestrator"/> so WELCOME can report the
    /// actual UDP audio port (which may differ from DefaultAudioPort).</summary>
    public int AudioPort { get; set; } = DiscoveryConstants.DefaultAudioPort;

    public event Action<ConnectedClient, ClientStatus>? ClientStatusReceived;

    public ControlServer(SessionManager sessions, LogService log)
    {
        _sessions = sessions;
        _log = log;
    }

    public void Start(int preferredPort = DiscoveryConstants.DefaultControlPort)
    {
        if (_listener is not null) throw new InvalidOperationException("Already started");

        // Preferred port may be busy (second instance, unrelated app). Try the
        // preferred one first, then the next few slots, then fall back to an
        // ephemeral port. The actual port is published via mDNS so clients adapt.
        System.Net.Sockets.SocketException? lastEx = null;
        for (var offset = 0; offset < 10 && _listener is null; offset++)
        {
            try
            {
                var candidate = preferredPort + offset;
                var listener = new TcpListener(IPAddress.Any, candidate);
                listener.Start();
                _listener = listener;
                Port = candidate;
                _log.Info("ControlServer", $"Listening on TCP {candidate}");
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                lastEx = ex;
            }
        }
        if (_listener is null)
        {
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _log.Warn("ControlServer",
                $"Preferred TCP {preferredPort}+ busy, using ephemeral port {Port}", lastEx);
        }

        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(tcp, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error("ControlServer", "Accept loop failed", ex);
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
        ConnectedClient? registered = null;
        try
        {
            tcp.NoDelay = true;
            // NOTE: don't wrap stream in `using` — it's shared with SendAsync from other threads.
            // The stream is owned by the TcpClient and disposed alongside it in the finally block.
            var stream = tcp.GetStream();

            var first = await MessageJson.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            if (first is not Hello hello)
            {
                _log.Warn("ControlServer", $"{remote}: expected HELLO, got {first?.GetType().Name ?? "null"}");
                return;
            }

            // Rate-limit auth attempts so the 6-digit session code can't be
            // brute-forced from the LAN (1M combinations · 1000 req/s = ~17 min).
            if (!_auth.AllowAttempt(remote.Address))
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("RATE_LIMITED"), ct);
                _log.Warn("ControlServer",
                    $"{remote}: rate-limited after {_auth.CurrentFailureCount(remote.Address)} failed attempts");
                return;
            }

            if (!_sessions.IsActive)
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("SESSION_NOT_ACTIVE"), ct);
                return;
            }
            if (!_sessions.ValidateCode(hello.Code))
            {
                _auth.RecordFailure(remote.Address);
                await MessageJson.WriteFrameAsync(stream, new AuthFail("INVALID_CODE"), ct);
                // Deliberately do NOT log the attempted code — tester pass flagged
                // session codes leaking into rolling log files.
                _log.Warn("ControlServer", $"{remote}: invalid code (name={hello.ClientName})");
                return;
            }
            _auth.RecordSuccess(remote.Address);

            var clientId = Guid.NewGuid().ToString("N")[..12];
            registered = _sessions.RegisterClient(new ConnectedClient
            {
                ClientId = clientId,
                ClientName = string.IsNullOrWhiteSpace(hello.ClientName) ? $"Client-{clientId}" : hello.ClientName,
                TcpClient = tcp,
                TcpEndpoint = remote,
            });

            var welcome = new Welcome(
                ClientId: clientId,
                AudioUdpPort: AudioPort,
                SampleRate: AudioFormat.SampleRate,
                Channels: AudioFormat.Channels,
                AudioCodec: "opus",
                ServerTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await SendOnStreamAsync(registered, stream, welcome, ct);

            // Message loop
            while (!ct.IsCancellationRequested && tcp.Connected)
            {
                var msg = await MessageJson.ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (msg is null) break;
                await HandleMessageAsync(registered, msg, stream, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            _log.Info("ControlServer", $"{remote}: connection closed ({ex.GetType().Name})");
        }
        catch (Exception ex)
        {
            _log.Error("ControlServer", $"{remote}: handler failed", ex);
        }
        finally
        {
            if (registered is not null)
                _sessions.UnregisterClient(registered.ClientId);
            try { tcp.Dispose(); } catch { }
        }
    }

    private async Task HandleMessageAsync(ConnectedClient client, ControlMessage msg, Stream stream, CancellationToken ct)
    {
        switch (msg)
        {
            case Ping ping:
                await SendOnStreamAsync(client, stream,
                    new Pong(ping.ClientTimeMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);
                break;

            case ClientStatus status:
                client.Volume = status.CurrentVolume;
                client.IsMuted = status.IsMuted;
                client.CurrentOutputDeviceId = status.CurrentDeviceId;
                client.LastBufferedMs = status.BufferedMs;
                client.LastStatusReceived = DateTimeOffset.Now;
                ClientStatusReceived?.Invoke(client, status);
                break;

            case OutputDevicesResponse resp:
                // Host stores this for UI (simplified: event hook would allow VM update)
                client.CurrentOutputDeviceId = resp.CurrentDeviceId;
                break;

            case AudioClientReady ready:
                client.AudioEndpoint = new IPEndPoint(client.TcpEndpoint.Address, ready.ClientUdpPort);
                _log.Info("ControlServer",
                    $"{client.ClientId} ({client.ClientName}) audio endpoint set to {client.AudioEndpoint}");
                break;

            default:
                // Unknown / unexpected messages are ignored
                break;
        }
    }

    public async Task SendAsync(ConnectedClient client, ControlMessage message, CancellationToken ct = default)
    {
        try
        {
            if (!client.TcpClient.Connected) return;
            var stream = client.TcpClient.GetStream();
            await SendOnStreamAsync(client, stream, message, ct);
        }
        catch (Exception ex)
        {
            _log.Warn("ControlServer", $"Send to {client.ClientId} failed", ex);
            _sessions.UnregisterClient(client.ClientId);
        }
    }

    private static async Task SendOnStreamAsync(ConnectedClient client, Stream stream, ControlMessage message, CancellationToken ct)
    {
        await client.WriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MessageJson.WriteFrameAsync(stream, message, ct);
        }
        finally
        {
            client.WriteLock.Release();
        }
    }

    public async Task BroadcastAsync(ControlMessage message, CancellationToken ct = default)
    {
        foreach (var c in _sessions.Clients)
            await SendAsync(c, message, ct);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        if (_acceptLoopTask is not null)
        {
            try { await _acceptLoopTask.ConfigureAwait(false); } catch { }
        }
        // Null out so the instance can be restarted via Start() again
        // (otherwise Start throws "Already started" after a stopped session).
        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoopTask = null;
        Port = 0;
    }
}
