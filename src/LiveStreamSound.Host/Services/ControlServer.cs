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
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    public int Port { get; private set; }

    public event Action<ConnectedClient, ClientStatus>? ClientStatusReceived;

    public ControlServer(SessionManager sessions, LogService log)
    {
        _sessions = sessions;
        _log = log;
    }

    public void Start(int port = DiscoveryConstants.DefaultControlPort)
    {
        if (_listener is not null) throw new InvalidOperationException("Already started");
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.Info("ControlServer", $"Listening on TCP {port}");
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

            if (!_sessions.IsActive)
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("SESSION_NOT_ACTIVE"), ct);
                return;
            }
            if (!_sessions.ValidateCode(hello.Code))
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("INVALID_CODE"), ct);
                _log.Warn("ControlServer", $"{remote}: invalid code '{hello.Code}' (name={hello.ClientName})");
                return;
            }

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
                AudioUdpPort: DiscoveryConstants.DefaultAudioPort,
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
    }
}
