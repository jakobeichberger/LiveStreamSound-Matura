using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Audio;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Listens on the audio UDP port, parses <see cref="AudioPacket"/> frames,
/// decodes Opus payloads and pushes resulting PCM frames into a <see cref="SyncBuffer"/>.
/// </summary>
public sealed class AudioStreamClient : IAsyncDisposable
{
    private readonly OpusDecoderService _decoder;
    private readonly SyncBuffer _buffer;
    private readonly LogService _log;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public int Port { get; private set; }
    public int ReceivedFrames { get; private set; }
    public int LostFrames { get; private set; }
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
        _udp = new UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = 1 << 18;
        var bound = (System.Net.IPEndPoint)_udp.Client.LocalEndPoint!;
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
                if (!AudioPacket.TryRead(result.Buffer, out var header, out var payload))
                {
                    _log.Debug("AudioStreamClient", "Invalid packet received");
                    continue;
                }

                if (lastSeq != 0 && header.SequenceNumber > lastSeq + 1)
                    LostFrames += (int)(header.SequenceNumber - lastSeq - 1);
                lastSeq = header.SequenceNumber;
                LastSequence = (int)lastSeq;
                ReceivedFrames++;

                int pcmLen;
                if (header.PayloadType == AudioPayloadType.Opus)
                {
                    pcmLen = _decoder.Decode(payload, pcmScratch);
                }
                else
                {
                    pcmLen = Math.Min(payload.Length, pcmScratch.Length);
                    payload.Slice(0, pcmLen).CopyTo(pcmScratch);
                }

                if (pcmLen > 0)
                {
                    var copy = new byte[pcmLen];
                    Array.Copy(pcmScratch, copy, pcmLen);
                    _buffer.Enqueue(header.SequenceNumber, header.ServerTimestampMs, copy);
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
