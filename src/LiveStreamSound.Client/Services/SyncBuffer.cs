using System.Collections.Concurrent;
using LiveStreamSound.Shared.Audio;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Timestamp-based jitter buffer for synchronized multi-client audio playback.
///
/// Algorithm: incoming audio frames carry the server-side capture timestamp.
/// We play each frame at (serverTimestamp + target latency) in local time.
/// If the local clock says we are past that moment → the frame is dropped (late).
/// If it is not yet time → the frame waits in the buffer.
/// </summary>
public sealed class SyncBuffer
{
    private readonly ClockSyncService _clockSync;
    private readonly int _targetLatencyMs;
    private readonly ConcurrentDictionary<uint, QueuedFrame> _frames = new();
    private int _packetsReceived;
    private int _packetsDropped;

    public int PacketsReceived => _packetsReceived;
    public int PacketsDropped => _packetsDropped;
    public int CurrentBufferedMs { get; private set; }

    public SyncBuffer(ClockSyncService clockSync, int targetLatencyMs = AudioFormat.JitterBufferMs)
    {
        _clockSync = clockSync;
        _targetLatencyMs = targetLatencyMs;
    }

    private readonly record struct QueuedFrame(long PlayLocalTimeMs, byte[] Pcm);

    public void Enqueue(uint sequence, long serverTimestampMs, byte[] pcmFrame)
    {
        Interlocked.Increment(ref _packetsReceived);
        var playLocal = _clockSync.ServerToLocal(serverTimestampMs) + _targetLatencyMs;
        _frames[sequence] = new QueuedFrame(playLocal, pcmFrame);
    }

    /// <summary>
    /// Drains all frames whose playback time has arrived, in sequence order.
    /// Late frames are discarded once clock-sync is established (so we don't drop the
    /// initial batch when offset is still zero and the clocks may disagree by >50ms).
    /// Call from the playback pump ~ every frame time.
    /// </summary>
    public IEnumerable<byte[]> DrainReady()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var synced = _clockSync.IsSynced;

        // Before the first pong, play frames in arrival order without waiting (and don't drop)
        // — multi-client sync is impossible anyway until we know the offset.
        if (!synced)
        {
            var unsynced = _frames.OrderBy(kv => kv.Key).ToList();
            foreach (var kv in unsynced)
            {
                _frames.TryRemove(kv.Key, out _);
                yield return kv.Value.Pcm;
            }
            CurrentBufferedMs = 0;
            yield break;
        }

        var ready = _frames
            .Where(kv => kv.Value.PlayLocalTimeMs <= now)
            .OrderBy(kv => kv.Key)
            .ToList();

        const int lateDropThresholdMs = 200;
        foreach (var kv in ready)
        {
            _frames.TryRemove(kv.Key, out _);
            if (kv.Value.PlayLocalTimeMs < now - lateDropThresholdMs)
            {
                Interlocked.Increment(ref _packetsDropped);
                continue;
            }
            yield return kv.Value.Pcm;
        }

        if (_frames.Count > 0)
        {
            CurrentBufferedMs = (int)Math.Max(0, _frames.Values.Max(f => f.PlayLocalTimeMs) - now);
        }
        else
        {
            CurrentBufferedMs = 0;
        }
    }

    public void Reset()
    {
        _frames.Clear();
        _packetsReceived = 0;
        _packetsDropped = 0;
        CurrentBufferedMs = 0;
    }
}
