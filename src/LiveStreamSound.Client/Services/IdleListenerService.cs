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

    public void Start(string clientFriendlyName)
    {
        if (_running) return;
        try
        {
            _listener = new TcpListener(IPAddress.Any, DiscoveryConstants.DefaultIdleClientPort);
            _listener.Start();

            _mc = new MulticastService(nics => nics.Where(NetworkInterfaceFilter.IsRealLan).ToList());
            _sd = new ServiceDiscovery(_mc);
            _mc.Start();
            var profile = new ServiceProfile(
                instanceName: Environment.MachineName,
                serviceName: DiscoveryConstants.MDnsClientServiceType,
                port: (ushort)DiscoveryConstants.DefaultIdleClientPort);
            profile.AddProperty(DiscoveryConstants.TxtVersionKey, DiscoveryConstants.ProtocolVersion.ToString());
            profile.AddProperty(DiscoveryConstants.TxtSessionNameKey, clientFriendlyName);
            _sd.Advertise(profile);

            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _running = true;
            _log.Info("IdleListener",
                $"Listening on TCP {DiscoveryConstants.DefaultIdleClientPort}; advertised as '{clientFriendlyName}'");
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

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener is not null)
            {
                var tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleInvitationAsync(tcp, ct), ct);
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
                var stream = tcp.GetStream();
                var msg = await MessageJson.ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (msg is not Invitation inv)
                {
                    _log.Debug("IdleListener", $"{remote}: expected Invitation, got {msg?.GetType().Name ?? "null"}");
                    return;
                }

                _log.Info("IdleListener", $"Invitation from '{inv.HostDisplayName}' at {inv.HostAddress}:{inv.HostControlPort} for session {inv.SessionCode}");

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
        catch (Exception ex)
        {
            _log.Warn("IdleListener", $"Invitation handler from {remote} failed", ex);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
