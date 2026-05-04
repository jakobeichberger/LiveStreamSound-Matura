using System.IO;
using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Client.Services;

public enum ControlClientState { Idle, Connecting, Authenticating, Connected, Reconnecting, Disconnected, Failed }

public sealed class ControlClient : IAsyncDisposable
{
    private readonly LogService _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private Timer? _pongWatchdog;
    public Welcome? Welcome { get; private set; }
    /// <summary>Session AEAD/MAC service derived from (code, salt) — set after a verified Welcome.</summary>
    public SessionCrypto? Crypto { get; private set; }
    public ControlClientState State { get; private set; } = ControlClientState.Idle;
    public IPAddress? HostAddress { get; private set; }
    public int HostControlPort { get; private set; }

    /// <summary>
    /// Last time we got *any* frame back from the host. Used by the pong-watchdog
    /// to treat a silent-but-not-yet-closed TCP connection as dead (which can
    /// happen with Wi-Fi dropouts — the socket stays "connected" for minutes
    /// before the OS times it out).
    /// </summary>
    public DateTimeOffset LastInboundFrameAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Seconds without an inbound frame after which we force-tear the TCP down.</summary>
    public int InboundSilenceTimeoutSeconds { get; set; } = 8;

    public event Action<ControlClientState>? StateChanged;
    public event Action<ControlMessage>? MessageReceived;
    public event Action<string>? ConnectionError;

    public ControlClient(LogService log) { _log = log; }

    private void SetState(ControlClientState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    /// <summary>
    /// Called by the orchestrator when it transitions into Reconnecting so the
    /// UI reflects the intent rather than the transient Disconnected state.
    /// </summary>
    internal void MarkReconnecting() => SetState(ControlClientState.Reconnecting);

    public async Task<Welcome?> ConnectAsync(
        IPAddress host,
        int controlPort,
        string code,
        string clientName,
        CancellationToken ct = default)
    {
        await DisposeAsync().ConfigureAwait(false);

        HostAddress = host;
        HostControlPort = controlPort;
        SetState(ControlClientState.Connecting);

        _tcp = new TcpClient { NoDelay = true };
        try
        {
            // Hard-cap the whole connect+HELLO+WELCOME exchange so a half-broken
            // host (TCP accepts but never responds) can't wedge the reconnect
            // loop indefinitely. 5 seconds is the worst-case Wi-Fi handshake
            // budget plus generous slack.
            using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, handshakeTimeout.Token);
            var handshakeCt = linked.Token;

            await _tcp.ConnectAsync(host, controlPort, handshakeCt).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            SetState(ControlClientState.Authenticating);

            await MessageJson.WriteFrameAsync(_stream,
                new Hello(code, clientName, DiscoveryConstants.ProtocolVersion), handshakeCt);

            var response = await MessageJson.ReadFrameAsync(_stream, handshakeCt);
            switch (response)
            {
                case Welcome welcome:
                    // Verify Welcome-MAC before accepting the connection. Defends
                    // against a fake host on the LAN that claims our session
                    // name in mDNS but doesn't know the session code.
                    var salt = SessionCrypto.FromHex(welcome.SessionSaltHex);
                    var mac = SessionCrypto.FromHex(welcome.WelcomeMacHex);
                    if (salt is null || mac is null || salt.Length < 8)
                    {
                        SetState(ControlClientState.Failed);
                        ConnectionError?.Invoke("AUTH_FAIL:WELCOME_MALFORMED");
                        return null;
                    }
                    var derivedCrypto = SessionCrypto.Derive(code, salt);
                    var canonical = SessionCrypto.CanonicalWelcomeBytes(
                        welcome.ClientId, welcome.AudioUdpPort, welcome.SampleRate,
                        welcome.Channels, welcome.AudioCodec, welcome.ServerTimeMs,
                        welcome.SessionSaltHex);
                    if (!derivedCrypto.VerifyMac(canonical, mac))
                    {
                        SetState(ControlClientState.Failed);
                        ConnectionError?.Invoke("AUTH_FAIL:WELCOME_MAC_INVALID");
                        _log.Warn("ControlClient",
                            $"Rejected WELCOME from {host}:{controlPort} — MAC invalid (host doesn't know session code)");
                        return null;
                    }

                    Welcome = welcome;
                    Crypto = derivedCrypto;
                    LastInboundFrameAt = DateTimeOffset.UtcNow;
                    SetState(ControlClientState.Connected);
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
                    // Watchdog: if we don't hear anything from the host (pong,
                    // status echo, anything) for InboundSilenceTimeoutSeconds,
                    // rip the socket down so the reconnect loop kicks in rather
                    // than letting a silently-dead Wi-Fi connection linger.
                    _pongWatchdog = new Timer(_ => CheckInboundSilence(), null,
                        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
                    _log.Info("ControlClient", $"Connected to {host}:{controlPort} as '{clientName}' (id={welcome.ClientId})");
                    return welcome;

                case AuthFail fail:
                    SetState(ControlClientState.Failed);
                    ConnectionError?.Invoke($"AUTH_FAIL:{fail.Reason}");
                    return null;

                default:
                    SetState(ControlClientState.Failed);
                    ConnectionError?.Invoke("UNEXPECTED_RESPONSE");
                    return null;
            }
        }
        catch (Exception ex)
        {
            _log.Error("ControlClient", $"Connect to {host}:{controlPort} failed", ex);
            SetState(ControlClientState.Failed);
            ConnectionError?.Invoke(ex.Message);
            return null;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var msg = await MessageJson.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                if (msg is null) break;
                LastInboundFrameAt = DateTimeOffset.UtcNow;
                MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            _log.Warn("ControlClient", "Read loop failed", ex);
        }
        finally
        {
            // Only flip to Disconnected if we're not already reconnecting — the
            // orchestrator may have already moved us to Reconnecting and we
            // don't want to regress the UI state.
            if (State == ControlClientState.Connected ||
                State == ControlClientState.Connecting ||
                State == ControlClientState.Authenticating)
                SetState(ControlClientState.Disconnected);
        }
    }

    private void CheckInboundSilence()
    {
        // Snapshot mutable refs locally so we don't race against ConnectAsync's
        // assignment of new _tcp / _stream. If the snapshot is stale (the TCP
        // we close was already replaced) the close-on-disposed-socket is a
        // no-op; the dangerous case is closing a *different* live socket,
        // which the State guard prevents because Connected only holds for
        // the in-flight session.
        if (State != ControlClientState.Connected) return;
        var snapshotTcp = _tcp;
        var snapshotStream = _stream;
        if (snapshotTcp is null) return;

        var silenceSeconds = (DateTimeOffset.UtcNow - LastInboundFrameAt).TotalSeconds;
        if (silenceSeconds >= InboundSilenceTimeoutSeconds)
        {
            _log.Warn("ControlClient",
                $"No inbound traffic for {silenceSeconds:F1}s — tearing down stale TCP so reconnect can take over");
            // Close underlying socket so the read loop exits. The orchestrator's
            // state-change handler decides whether to reconnect.
            try { snapshotTcp.Close(); } catch { }
            try { snapshotStream?.Close(); } catch { }
        }
    }

    public async Task SendAsync(ControlMessage message, CancellationToken ct = default)
    {
        var stream = _stream;
        if (stream is null) return;
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MessageJson.WriteFrameAsync(stream, message, ct);
        }
        catch (ObjectDisposedException) { /* disconnected mid-send, drop */ }
        catch (IOException) { /* disconnected mid-send, drop */ }
        catch (Exception ex)
        {
            _log.Warn("ControlClient", "Send failed", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose the watchdog FIRST so a tick mid-Dispose can't observe a
        // half-torn-down socket and close a freshly-assigned one from a
        // concurrent ConnectAsync. Use DisposeAsync (System.Threading.Timer
        // implements IAsyncDisposable since .NET 8) so we deterministically
        // wait for any executing callback to complete — Timer.Dispose() is
        // non-blocking and a tick scheduled microseconds before us would
        // run concurrently with the rest of teardown.
        if (_pongWatchdog is not null)
        {
            await _pongWatchdog.DisposeAsync().ConfigureAwait(false);
            _pongWatchdog = null;
        }

        try { _cts?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); } catch { }
        }
        _stream = null;
        _tcp = null;
        _cts?.Dispose();
        _cts = null;
        _readLoop = null;
        Welcome = null;
        Crypto = null;
        // Reset silence-tracking so a fast reconnect doesn't see a stale
        // "last frame at" from the previous connection.
        LastInboundFrameAt = DateTimeOffset.MinValue;
    }
}
