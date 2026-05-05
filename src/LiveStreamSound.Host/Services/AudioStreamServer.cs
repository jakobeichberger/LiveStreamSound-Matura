using System.Buffers;
using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;
using SharedCrypto = LiveStreamSound.Shared.Protocol.SessionCrypto;

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
                // Read the ACTUAL bound port — when preferredPort is 0
                // (OS-assigned ephemeral), `candidate` is 0 too and would
                // be a useless port number. LocalEndPoint works for both.
                Port = ((IPEndPoint)u.Client.LocalEndPoint!).Port;
                _log.Info("AudioStreamServer", $"UDP audio server on port {Port}");
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
        var crypto = _sessions.Crypto;
        if (crypto is null) return; // session ended mid-broadcast — drop frame
        var seq = Interlocked.Increment(ref _sequence);

        // AEAD-encrypted payload layout on the wire:
        //   [original AudioPacket header (20 bytes, version=1, magic=LSSA)]
        //   [ciphertext (= plaintext length)]
        //   [16-byte AES-GCM auth tag]
        // Header.PayloadLength reflects ciphertext length so a v1 client
        // (rejected at HELLO with PROTOCOL_VERSION_MISMATCH anyway) sees
        // a structurally valid packet but garbage Opus.
        var plaintextLen = encodedPayload.Length;
        var totalLen = AudioPacket.HeaderSize + plaintextLen + SessionCrypto.TagSizeBytes;
        var packet = ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            // Write the header first with the meaningful payload length
            // (ciphertext length, NOT including the tag — we put the tag after).
            AudioPacket.Write(packet.AsSpan(0, AudioPacket.HeaderSize + plaintextLen),
                new AudioPacketHeader(seq, serverTimestampMs, payloadType, (ushort)plaintextLen),
                encodedPayload.Span);

            // Encrypt in-place over the just-written plaintext slice.
            var ciphertextSpan = packet.AsSpan(AudioPacket.HeaderSize, plaintextLen);
            var tagSpan = packet.AsSpan(AudioPacket.HeaderSize + plaintextLen, SessionCrypto.TagSizeBytes);
            crypto.EncryptAudio(seq, encodedPayload.Span, ciphertextSpan, tagSpan);

            var udp = _udp;
            var sendTasks = _sessions.ActiveClients
                .Where(c => c.AudioEndpoint is not null)
                .Select(c => SendOneAsync(udp, packet, totalLen, c))
                .ToArray();
            await Task.WhenAll(sendTasks).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    private async Task SendOneAsync(UdpClient udp, byte[] packet, int len, ConnectedClient client)
    {
        try
        {
            await udp.SendAsync(packet, len, client.AudioEndpoint!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log per-client failures at Debug — at 50 fps a transiently-bad
            // NIC would otherwise spam the log. The diagnostics service will
            // surface persistent loss separately as a connection issue.
            _log.Debug("AudioStreamServer", $"UDP send to {client.ClientId} failed: {ex.Message}");
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
