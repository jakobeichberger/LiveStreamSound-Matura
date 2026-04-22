using System.Buffers;
using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Sends encoded audio frames as UDP packets to every connected client's audio endpoint.
/// Writes a fresh packet per frame; no retransmission (UDP). Sequence number + server
/// timestamp in the header let clients do sync playback.
/// </summary>
public sealed class AudioStreamServer : IDisposable
{
    private readonly SessionManager _sessions;
    private readonly LogService _log;
    private UdpClient? _udp;
    private uint _sequence;
    public int Port { get; private set; }

    public AudioStreamServer(SessionManager sessions, LogService log)
    {
        _sessions = sessions;
        _log = log;
    }

    public void Start(int preferredPort = DiscoveryConstants.DefaultAudioPort)
    {
        SocketException? lastEx = null;
        for (var offset = 0; offset < 10 && _udp is null; offset++)
        {
            try
            {
                var candidate = preferredPort + offset;
                var u = new UdpClient(candidate);
                u.Client.SendBufferSize = 1 << 18;
                _udp = u;
                Port = candidate;
                _log.Info("AudioStreamServer", $"UDP audio server on port {candidate}");
            }
            catch (SocketException ex) { lastEx = ex; }
        }
        if (_udp is null)
        {
            var u = new UdpClient(0);
            u.Client.SendBufferSize = 1 << 18;
            _udp = u;
            Port = ((IPEndPoint)u.Client.LocalEndPoint!).Port;
            _log.Warn("AudioStreamServer",
                $"Preferred UDP {preferredPort}+ busy, using ephemeral port {Port}", lastEx);
        }
    }

    public async Task BroadcastFrameAsync(
        AudioPayloadType payloadType,
        ReadOnlyMemory<byte> encodedPayload,
        long serverTimestampMs,
        CancellationToken ct = default)
    {
        if (_udp is null) return;
        var seq = Interlocked.Increment(ref _sequence);
        var totalLen = AudioPacket.HeaderSize + encodedPayload.Length;

        // ArrayPool-rented buffer keeps the 50 fps broadcast pipeline off the GC.
        var packet = ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            AudioPacket.Write(packet.AsSpan(0, totalLen),
                new AudioPacketHeader(seq, serverTimestampMs, payloadType, (ushort)encodedPayload.Length),
                encodedPayload.Span);

            // ActiveClients skips clients whose TCP just dropped (currently in
            // grace period) so we don't waste bandwidth spraying UDP at an
            // endpoint whose owner isn't listening.
            foreach (var client in _sessions.ActiveClients)
            {
                if (client.AudioEndpoint is null) continue;
                try
                {
                    await _udp.SendAsync(packet, totalLen, client.AudioEndpoint).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("AudioStreamServer", $"UDP send to {client.ClientId} failed", ex);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    /// <summary>
    /// Called by the Host once the client advertises its UDP endpoint (we infer it from the
    /// TCP source IP + WELCOME-declared port). Subclass could do hole-punching if needed.
    /// </summary>
    public void AssignAudioEndpointFromTcp(ConnectedClient client, int audioPort)
    {
        client.AudioEndpoint = new IPEndPoint(client.TcpEndpoint.Address, audioPort);
        _log.Info("AudioStreamServer",
            $"Assigned audio endpoint {client.AudioEndpoint} for {client.ClientName}");
    }

    public void Dispose()
    {
        try { _udp?.Dispose(); } catch { }
        _udp = null;
    }
}
