using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LiveStreamSound.Shared.Audio;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Listens on the audio UDP port, parses <see cref="AudioPacket"/> frames,
/// AEAD-decrypts (since protocol v2), decodes Opus payloads and pushes
/// resulting PCM frames into a <see cref="SyncBuffer"/>.
///
/// <para>
/// Source-IP filter: drops every packet whose source IP doesn't match the
/// expected host address. Without this filter, anyone on the LAN could
/// inject malformed/random UDP that drowns out the real stream or fuzzes
/// the Opus decoder. Combined with AEAD this is belt-and-suspenders — the
/// crypto rejects forged content but the IP filter saves us the CPU of
/// even attempting decryption.
/// </para>
/// </summary>
public sealed class AudioStreamClient : IAsyncDisposable
{
    private readonly OpusDecoderService _decoder;
    private readonly SyncBuffer _buffer;
    private readonly LogService _log;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// IP address packets are accepted from. Set by the orchestrator after
    /// WELCOME. Anything from a different source is dropped without parsing.
    /// </summary>
    public IPAddress? ExpectedSource { get; set; }

    /// <summary>
    /// Session crypto for AEAD decryption — set by the orchestrator after a
    /// verified WELCOME. Null = run in v1-pre-encryption mode (only used by
    /// older tests; production always runs with crypto).
    /// </summary>
    public SessionCrypto? Crypto { get; set; }

    public int Port { get; private set; }
    public int ReceivedFrames { get; private set; }
    public int LostFrames { get; private set; }
    public int DroppedSpoofedFrames { get; private set; }
    public int DroppedAuthFailedFrames { get; private set; }
    public int LastSequence { get; private set; }

    public AudioStreamClient(OpusDecoderService decoder, SyncBuffer buffer, LogService log)
    {
        _decoder = decoder;
        _buffer = buffer;
        _log = log;
    }

    public async Task StartAsync(int port = 0)
    {
        await Stop().ConfigureAwait(false);
        // port = 0 → ephemeral (OS-picked) so multiple clients on the same
        // machine don't collide with a host-bound 5001.
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = 1 << 18;
        var bound = (IPEndPoint)_udp.Client.LocalEndPoint!;
        Port = bound.Port;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _log.Info("AudioStreamClient", $"Listening on UDP {Port} (requested {port})");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var lastSeq = 0u;
        var pcmScratch = new byte[AudioFormat.BytesPerPcmFrame];
        try
        {
            while (!ct.IsCancellationRequested && _udp is not null)
            {
                var result = await _udp.ReceiveAsync(ct).ConfigureAwait(false);

                // Source-IP filter: reject packets that didn't come from the
                // host we negotiated with. Defends against spoof-flood and
                // off-LAN injection. Counts dropped packets as a diagnostic.
                if (ExpectedSource is not null &&
                    !result.RemoteEndPoint.Address.Equals(ExpectedSource))
                {
                    DroppedSpoofedFrames++;
                    continue;
                }

                if (!AudioPacket.TryRead(result.Buffer, out var header, out var ciphertextPlusTag))
                {
                    _log.Debug("AudioStreamClient", "Invalid packet received");
                    continue;
                }

                ReadOnlySpan<byte> opusPayload;
                byte[]? rentedPlain = null;
                try
                {
                    if (Crypto is not null)
                    {
                        // v2 wire layout: [header][ciphertext][16-byte tag]
                        // header.PayloadLength is ciphertext length.
                        if (ciphertextPlusTag.Length < SessionCrypto.TagSizeBytes)
                        {
                            _log.Debug("AudioStreamClient", "Truncated AEAD packet");
                            continue;
                        }
                        var ciphertextLen = header.PayloadLength;
                        // Note: AudioPacket.TryRead returned only `payloadLength` bytes
                        // (the cipher), so the tag follows in the original buffer
                        // immediately after. Recompute span over the wire buffer.
                        var wireOffset = AudioPacket.HeaderSize + ciphertextLen;
                        if (result.Buffer.Length < wireOffset + SessionCrypto.TagSizeBytes)
                        {
                            _log.Debug("AudioStreamClient", "AEAD tag missing");
                            continue;
                        }
                        var ciphertext = new ReadOnlySpan<byte>(result.Buffer, AudioPacket.HeaderSize, ciphertextLen);
                        var tag = new ReadOnlySpan<byte>(result.Buffer, wireOffset, SessionCrypto.TagSizeBytes);
                        rentedPlain = ArrayPool<byte>.Shared.Rent(ciphertextLen);
                        try
                        {
                            Crypto.DecryptAudio(header.SequenceNumber, ciphertext, tag, rentedPlain.AsSpan(0, ciphertextLen));
                        }
                        catch (CryptographicException)
                        {
                            // Tag verification failed → packet was forged or
                            // corrupted in transit. Drop silently — never log
                            // tag-mismatch detail (CWE-209 info leak).
                            DroppedAuthFailedFrames++;
                            continue;
                        }
                        opusPayload = new ReadOnlySpan<byte>(rentedPlain, 0, ciphertextLen);
                    }
                    else
                    {
                        opusPayload = ciphertextPlusTag;
                    }

                    if (lastSeq != 0 && header.SequenceNumber > lastSeq + 1)
                        LostFrames += (int)(header.SequenceNumber - lastSeq - 1);
                    lastSeq = header.SequenceNumber;
                    LastSequence = (int)lastSeq;
                    ReceivedFrames++;

                    int pcmLen;
                    if (header.PayloadType == AudioPayloadType.Opus)
                    {
                        pcmLen = _decoder.Decode(opusPayload, pcmScratch);
                    }
                    else
                    {
                        pcmLen = Math.Min(opusPayload.Length, pcmScratch.Length);
                        opusPayload.Slice(0, pcmLen).CopyTo(pcmScratch);
                    }

                    if (pcmLen > 0)
                    {
                        // Rent from ArrayPool instead of `new byte[pcmLen]` so the
                        // 50 fps receive loop doesn't churn ~3.8 KB / frame through
                        // Gen0. SyncBuffer returns the buffer to the pool when the
                        // frame is drained or evicted.
                        var pooled = ArrayPool<byte>.Shared.Rent(pcmLen);
                        Array.Copy(pcmScratch, pooled, pcmLen);
                        _buffer.Enqueue(header.SequenceNumber, header.ServerTimestampMs, pooled, pcmLen);
                    }
                }
                finally
                {
                    if (rentedPlain is not null)
                        ArrayPool<byte>.Shared.Return(rentedPlain);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Warn("AudioStreamClient", "Receive loop ended", ex);
        }
    }

    public async Task Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { }
        }
        _udp?.Dispose();
        _udp = null;
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await Stop().ConfigureAwait(false);
}
