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

    /// <summary>Cap on simultaneously-accepted-but-not-authenticated connections.
    /// Defends against SlowLoris-style resource exhaustion.</summary>
    private const int MaxConcurrentPreAuthConnections = 50;
    private int _preAuthInFlight;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (Interlocked.Increment(ref _preAuthInFlight) > MaxConcurrentPreAuthConnections)
                {
                    Interlocked.Decrement(ref _preAuthInFlight);
                    try { tcp.Close(); } catch { }
                    _log.Debug("ControlServer", "Rejected — too many concurrent pre-auth connections");
                    continue;
                }
                _ = Task.Run(async () =>
                {
                    try { await HandleClientAsync(tcp, ct).ConfigureAwait(false); }
                    finally { Interlocked.Decrement(ref _preAuthInFlight); }
                }, ct);
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

            // Per-connection 5-second pre-HELLO timeout so a SlowLoris peer
            // (TCP-accept, no HELLO) can't tie up a thread-pool task forever.
            using var helloTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct, helloTimeout.Token);
            var first = await MessageJson.ReadFrameAsync(stream, helloCts.Token).ConfigureAwait(false);
            if (first is not Hello hello)
            {
                _log.Warn("ControlServer", $"{remote}: expected HELLO, got {first?.GetType().Name ?? "null"}");
                return;
            }

            // Protocol-version compatibility check FIRST so a mismatched client
            // gets a clear error instead of trying to brute-force a code with
            // a wire format that's about to be rejected.
            if (hello.ProtocolVersion != DiscoveryConstants.ProtocolVersion)
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("PROTOCOL_VERSION_MISMATCH"), ct);
                _log.Warn("ControlServer",
                    $"{remote}: protocol version {hello.ProtocolVersion} != server {DiscoveryConstants.ProtocolVersion}");
                return;
            }

            // Sanitize ClientName before any further use: cap at 64 chars,
            // strip control characters that could break log parsing
            // (CWE-117 log injection defense).
            var sanitizedName = SanitizeClientName(hello.ClientName);

            // Rate-limit auth attempts so the 6-digit session code can't be
            // brute-forced from the LAN. Unified-error path below (no enumeration).
            if (!_auth.AllowAttempt(remote.Address))
            {
                await MessageJson.WriteFrameAsync(stream, new AuthFail("AUTH_FAILED"), ct);
                _log.Warn("ControlServer",
                    $"{remote}: rate-limited after {_auth.CurrentFailureCount(remote.Address)} failed attempts");
                return;
            }

            if (!_sessions.IsActive || !_sessions.ValidateCode(hello.Code))
            {
                if (_sessions.IsActive)
                {
                    _auth.RecordFailure(remote.Address);
                    _log.Warn("ControlServer", $"{remote}: invalid code (name={sanitizedName})");
                }
                else
                {
                    // Don't increment auth-tracker for "no session" — but also
                    // return the same opaque error so attackers can't enumerate
                    // session-active vs wrong-code by response timing/text.
                    _log.Info("ControlServer", $"{remote}: hello while no session active");
                }
                // Small artificial delay defends against rapid IP-rotation
                // brute-force: legit users won't notice 100ms, attackers
                // grinding 75k attempts/hour drop ~10×.
                await Task.Delay(TimeSpan.FromMilliseconds(120), ct);
                await MessageJson.WriteFrameAsync(stream, new AuthFail("AUTH_FAILED"), ct);
                return;
            }
            _auth.RecordSuccess(remote.Address);

            var effectiveName = string.IsNullOrWhiteSpace(sanitizedName)
                ? $"Client-{Guid.NewGuid().ToString("N")[..8]}"
                : sanitizedName;

            // First check if this is a rejoining client (same name, TCP
            // dropped within the grace period). If so, reuse the old
            // ClientId + preserved volume/mute/device settings so the teacher
            // doesn't lose any per-client config across a WLAN hiccup.
            //
            // SECURITY: rejoin requires the source IP to match the original
            // connect — otherwise a same-name attacker on a different machine
            // could "inherit" a legitimate client's slot during the 60s grace
            // window and silently receive the stream. Identical source IP
            // doesn't prove identity strongly, but raises the bar from
            // "anyone on LAN" to "co-tenant of the same NAT/IP" which on a
            // school LAN is a meaningful restriction.
            var existing = _sessions.TryFindReconnectingByName(effectiveName);
            if (existing is not null && existing.TcpEndpoint.Address.Equals(remote.Address))
            {
                existing.TcpClient = tcp;
                existing.TcpEndpoint = remote;
                existing.WriteLock = new SemaphoreSlim(1, 1);
                _sessions.FinalizeRejoin(existing);
                registered = existing;
            }
            else if (existing is not null)
            {
                _log.Warn("ControlServer",
                    $"{remote}: rejoin name match for {effectiveName} but source IP differs " +
                    $"({existing.TcpEndpoint.Address} → {remote.Address}); treating as new client");
                var clientId = Guid.NewGuid().ToString("N")[..12];
                registered = _sessions.RegisterClient(new ConnectedClient
                {
                    ClientId = clientId,
                    ClientName = effectiveName,
                    TcpClient = tcp,
                    TcpEndpoint = remote,
                });
            }
            else
            {
                var clientId = Guid.NewGuid().ToString("N")[..12];
                registered = _sessions.RegisterClient(new ConnectedClient
                {
                    ClientId = clientId,
                    ClientName = effectiveName,
                    TcpClient = tcp,
                    TcpEndpoint = remote,
                });
            }

            var serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var saltHex = _sessions.SessionSaltHex;
            var crypto = _sessions.Crypto
                ?? throw new InvalidOperationException("Crypto not initialised after StartSession");
            var canonical = SessionCrypto.CanonicalWelcomeBytes(
                registered.ClientId, AudioPort, AudioFormat.SampleRate,
                AudioFormat.Channels, "opus", serverTimeMs, saltHex);
            var welcomeMacHex = SessionCrypto.ToHex(crypto.Mac(canonical));

            var welcome = new Welcome(
                ClientId: registered.ClientId,
                AudioUdpPort: AudioPort,
                SampleRate: AudioFormat.SampleRate,
                Channels: AudioFormat.Channels,
                AudioCodec: "opus",
                ServerTimeMs: serverTimeMs,
                SessionSaltHex: saltHex,
                WelcomeMacHex: welcomeMacHex);
            await SendOnStreamAsync(registered, stream, welcome, ct);

            // For a rejoin: push the preserved volume/mute/device back down so
            // the client restores the exact state the teacher had configured.
            if (registered.Volume != 1.0f)
                await SendOnStreamAsync(registered, stream, new SetVolume(registered.Volume), ct);
            if (registered.IsMuted)
                await SendOnStreamAsync(registered, stream, new SetMute(true), ct);
            if (!string.IsNullOrEmpty(registered.CurrentOutputDeviceId))
                await SendOnStreamAsync(registered, stream, new SetOutputDevice(registered.CurrentOutputDeviceId), ct);

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
            // Only soft-unregister if THIS connection is still the one the
            // session knows about. Otherwise we're a stale per-connection
            // task that finally-ran after a fresh rejoin already swapped the
            // ConnectedClient's TcpClient for a new socket — flipping back
            // to IsReconnecting=true here would kick the rejoined client.
            if (registered is not null && ReferenceEquals(registered.TcpClient, tcp))
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

    /// <summary>
    /// Truncate + control-character-strip an attacker-controlled ClientName so
    /// it can't inject newlines / log-format-breaking sequences and so a
    /// 1-MiB malicious value doesn't propagate through ToLowerInvariant /
    /// dictionary lookups. Defense for CWE-117 (log injection) and DoS via
    /// large name strings.
    /// </summary>
    private static string SanitizeClientName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        const int maxLen = 64;
        var trimmed = raw.Length > maxLen ? raw[..maxLen] : raw;
        var sb = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            // Allow printable ASCII + common Unicode letters; reject control
            // chars (newline, tab, escape sequences) which would break log
            // line semantics.
            if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c)) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
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
