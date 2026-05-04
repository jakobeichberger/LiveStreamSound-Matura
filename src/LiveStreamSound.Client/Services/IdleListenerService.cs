using System.IO;
using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// While the Client app is idle (not connected to a Host), this service
/// advertises the mDNS service type <c>_lssclient._tcp</c> and listens on
/// TCP port 5002 for an <see cref="Invitation"/> from a Host. The callback
/// <see cref="OnInvitation"/> returns true/false to accept or decline; the
/// reply is sent back on the same TCP connection and the socket closed.
/// On accept, the Client app should establish a normal HELLO connection
/// to the Host — this service does not do that itself.
/// </summary>
public sealed class IdleListenerService : IAsyncDisposable
{
    private readonly LogService _log;
    private TcpListener? _listener;
    private ServiceDiscovery? _sd;
    private MulticastService? _mc;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private bool _running;

    /// <summary>
    /// Invoked when an invitation arrives. Return true to accept, false to decline.
    /// The handler can await UI interaction.
    /// </summary>
    public Func<Invitation, Task<bool>>? OnInvitation { get; set; }

    public bool IsRunning => _running;

    public IdleListenerService(LogService log) { _log = log; }

    /// <summary>The dynamic TCP port this listener bound to (0 until Start succeeds).</summary>
    public int BoundPort { get; private set; }

    public void Start(string clientFriendlyName)
    {
        if (_running) return;
        try
        {
            // Bind to OS-assigned dynamic port; clients don't need a fixed port
            // since hosts discover them via mDNS SRV records which carry the port.
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _mc = new MulticastService(nics => nics.Where(NetworkInterfaceFilter.IsRealLan).ToList());
            _sd = new ServiceDiscovery(_mc);
            _mc.Start();
            var profile = new ServiceProfile(
                instanceName: Environment.MachineName,
                serviceName: DiscoveryConstants.MDnsClientServiceType,
                port: (ushort)BoundPort);
            profile.AddProperty(DiscoveryConstants.TxtVersionKey, DiscoveryConstants.ProtocolVersion.ToString());
            profile.AddProperty(DiscoveryConstants.TxtSessionNameKey, clientFriendlyName);
            _sd.Advertise(profile);

            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _running = true;
            _log.Info("IdleListener",
                $"Listening on TCP {BoundPort}; advertised as '{clientFriendlyName}'");
        }
        catch (Exception ex)
        {
            _log.Warn("IdleListener", "Start failed (manual connect to host still works)", ex);
            _running = false;
        }
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _sd?.Unadvertise(); } catch { }
        try { _sd?.Dispose(); } catch { }
        try { _mc?.Dispose(); } catch { }
        _sd = null;
        _mc = null;
        _listener = null;
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
            _acceptLoop = null;
        }
        _cts?.Dispose();
        _cts = null;
        _log.Info("IdleListener", "Stopped");
    }

    /// <summary>Cap on simultaneously-accepted-but-not-yet-handled invitations.
    /// Defends against a SlowLoris-style attack where a malicious peer opens
    /// many TCP sockets but never sends an Invitation frame.</summary>
    private const int MaxConcurrentInvitations = 16;
    private int _inFlightInvitations;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener is not null)
            {
                var tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                // Reject new connections beyond the in-flight cap so an
                // adversary can't exhaust thread-pool tasks via half-open sockets.
                if (Interlocked.Increment(ref _inFlightInvitations) > MaxConcurrentInvitations)
                {
                    Interlocked.Decrement(ref _inFlightInvitations);
                    try { tcp.Close(); } catch { }
                    _log.Debug("IdleListener", "Rejected — too many concurrent invitations");
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try { await HandleInvitationAsync(tcp, ct).ConfigureAwait(false); }
                    finally { Interlocked.Decrement(ref _inFlightInvitations); }
                }, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _log.Warn("IdleListener", "Accept loop ended", ex);
        }
    }

    private async Task HandleInvitationAsync(TcpClient tcp, CancellationToken ct)
    {
        var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            using (tcp)
            {
                tcp.NoDelay = true;
                tcp.ReceiveTimeout = 5000;
                var stream = tcp.GetStream();
                // Bound the read with a 5-second timeout so a peer that
                // accepts the TCP but never sends a frame can't hold the
                // handler open forever. Combined with the in-flight cap above,
                // this caps total resource exposure to ~16 × 5 sec = 80 sec
                // worst case before all slots free up.
                using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, readTimeout.Token);
                var msg = await MessageJson.ReadFrameAsync(stream, linked.Token).ConfigureAwait(false);
                if (msg is not Invitation inv)
                {
                    _log.Debug("IdleListener", $"{remote}: expected Invitation, got {msg?.GetType().Name ?? "null"}");
                    return;
                }

                // Sanity-bound the attacker-controlled fields BEFORE they go
                // into the UI dialog or any logging. Caps prevent a 1-MiB
                // HostDisplayName from hanging the UI on TextBlock layout, and
                // strip control characters that could break log parsing.
                inv = SanitizeInvitation(inv);

                if (!IsValidIPv4OrV6(inv.HostAddress))
                {
                    _log.Debug("IdleListener", $"{remote}: rejected — invalid HostAddress");
                    return;
                }
                if (inv.HostControlPort is < 1 or > 65535)
                {
                    _log.Debug("IdleListener", $"{remote}: rejected — bad HostControlPort {inv.HostControlPort}");
                    return;
                }

                // Session code deliberately omitted — file logs shouldn't carry the secret.
                _log.Info("IdleListener",
                    $"Invitation from '{inv.HostDisplayName}' at {inv.HostAddress}:{inv.HostControlPort}");

                bool accepted = false;
                if (OnInvitation is not null)
                {
                    try { accepted = await OnInvitation.Invoke(inv).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _log.Warn("IdleListener", "Invitation handler threw", ex);
                        accepted = false;
                    }
                }

                var reason = accepted ? null : "declined";
                try
                {
                    // Deliberately CancellationToken.None: the host is waiting for
                    // our response and racing the listener's lifecycle cancellation
                    // (StopAsync fires the listener's ct once we start connecting)
                    // would leave the host reading `null` and log a spurious warning.
                    await MessageJson.WriteFrameAsync(stream, new InvitationResponse(accepted, reason), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /* client may have closed socket */ }
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { /* read-timeout — silent drop, attacker shouldn't get a log line */ }
        catch (Exception ex)
        {
            _log.Warn("IdleListener", $"Invitation handler from {remote} failed", ex);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Cap attacker-controlled invitation fields. CWE-117 / CWE-1284 defense.</summary>
    private static Invitation SanitizeInvitation(Invitation inv)
    {
        return new Invitation(
            SessionCode: Trunc(inv.SessionCode, 12),
            HostAddress: Trunc(inv.HostAddress, 64),
            HostControlPort: inv.HostControlPort,
            HostDisplayName: SanitizeForUi(inv.HostDisplayName, 80));
    }

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    private static string SanitizeForUi(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var truncated = s.Length <= max ? s : s[..max];
        var sb = new System.Text.StringBuilder(truncated.Length);
        foreach (var c in truncated)
        {
            if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c)) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static bool IsValidIPv4OrV6(string s) =>
        !string.IsNullOrEmpty(s) && System.Net.IPAddress.TryParse(s, out _);
}
