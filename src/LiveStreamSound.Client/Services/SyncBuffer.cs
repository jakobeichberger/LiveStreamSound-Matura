using System.Buffers;
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
///
/// Frame buffers are ArrayPool-rented by the caller (AudioStreamClient) and
/// either returned via Drain (consumer is responsible for returning after
/// playback) or returned by us when a frame is dropped/evicted.
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

    /// <summary>
    /// One queued audio frame. <see cref="Pcm"/> is rented from
    /// <see cref="ArrayPool{Byte}"/>; <see cref="Length"/> is the meaningful
    /// payload size (the rented array may be larger). Consumers MUST return
    /// the buffer to the pool after writing it to playback, or the buffer
    /// owner here returns it on drop.
    /// </summary>
    public readonly record struct QueuedFrame(long PlayLocalTimeMs, byte[] Pcm, int Length);

    /// <summary>
    /// Enqueue a pooled buffer. The caller transfers ownership of <paramref name="pooledPcm"/>
    /// to the buffer; we'll return it to the ArrayPool when the frame is
    /// consumed or dropped.
    /// </summary>
    public void Enqueue(uint sequence, long serverTimestampMs, byte[] pooledPcm, int length)
    {
        Interlocked.Increment(ref _packetsReceived);
        var playLocal = _clockSync.ServerToLocal(serverTimestampMs) + _targetLatencyMs;
        // If a duplicate seq is enqueued, return the previous buffer to pool
        // before overwriting (otherwise the old rental leaks).
        if (_frames.TryGetValue(sequence, out var existing))
            ArrayPool<byte>.Shared.Return(existing.Pcm);
        _frames[sequence] = new QueuedFrame(playLocal, pooledPcm, length);
    }

    /// <summary>
    /// Backwards-compatible overload for tests + legacy callers that hand in a
    /// non-pooled byte[]. We wrap the buffer in a pooled one so the rest of the
    /// code path uniformly returns to the pool. The caller's array reference
    /// becomes stale and should not be used afterwards.
    /// </summary>
    public void Enqueue(uint sequence, long serverTimestampMs, byte[] pcmFrame)
    {
        var pooled = ArrayPool<byte>.Shared.Rent(pcmFrame.Length);
        Array.Copy(pcmFrame, pooled, pcmFrame.Length);
        Enqueue(sequence, serverTimestampMs, pooled, pcmFrame.Length);
    }

    /// <summary>
    /// Drains all frames whose playback time has arrived, in sequence order.
    /// Late frames are discarded once clock-sync is established (so we don't drop the
    /// initial batch when offset is still zero and the clocks may disagree by >50ms).
    /// Call from the playback pump ~ every frame time.
    /// <para>
    /// The yielded byte[] is a pooled buffer — the consumer is responsible for
    /// calling <see cref="ArrayPool{Byte}.Shared.Return"/> after the frame has
    /// been written to playback. <see cref="DrainReadyAsPooled"/> is preferred
    /// for new code as it carries the explicit length.
    /// </para>
    /// </summary>
    public IEnumerable<byte[]> DrainReady()
    {
        foreach (var (pcm, _) in DrainReadyAsPooled())
            yield return pcm;
    }

    /// <summary>
    /// Drain variant that exposes both the pooled buffer and its meaningful
    /// length. Consumer MUST return the buffer to the ArrayPool after use.
    /// </summary>
    public IEnumerable<(byte[] Pcm, int Length)> DrainReadyAsPooled()
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
                if (_frames.TryRemove(kv.Key, out var removed))
                    yield return (removed.Pcm, removed.Length);
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
            if (!_frames.TryRemove(kv.Key, out var frame)) continue;
            if (frame.PlayLocalTimeMs < now - lateDropThresholdMs)
            {
                Interlocked.Increment(ref _packetsDropped);
                ArrayPool<byte>.Shared.Return(frame.Pcm);
                continue;
            }
            yield return (frame.Pcm, frame.Length);
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
        // Return any in-flight pooled buffers before clearing.
        foreach (var f in _frames.Values)
        {
            try { ArrayPool<byte>.Shared.Return(f.Pcm); } catch { }
        }
        _frames.Clear();
        _packetsReceived = 0;
        _packetsDropped = 0;
        CurrentBufferedMs = 0;
    }
}
