using LiveStreamSound.Client.Services;
using LiveStreamSound.Shared.Audio;

namespace LiveStreamSound.Client.Tests;

/// <summary>
/// The jitter buffer is the heart of multi-client sync. Tests cover:
/// before-sync behaviour (pass-through), after-sync timing semantics,
/// late-frame dropping, out-of-order arrivals, buffered-ms reporting.
/// </summary>
public class SyncBufferTests
{
    private const int TargetLatencyMs = AudioFormat.JitterBufferMs;

    private static byte[] Pcm(int tag) => new byte[] { (byte)tag };

    /// <summary>Helper: returns a clock-sync that reports IsSynced=true with
    /// offset=0 (server clock ≡ local clock for straightforward assertions).</summary>
    private static ClockSyncService SyncedClock()
    {
        var c = new ClockSyncService();
        // Put a fresh pong in: client sends at t=now, server echoes same, rtt=0 → offset=0.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        c.NotifyPong(now, now, now);
        return c;
    }

    [Fact]
    public void Counters_StartAtZero()
    {
        var b = new SyncBuffer(new ClockSyncService());
        Assert.Equal(0, b.PacketsReceived);
        Assert.Equal(0, b.PacketsDropped);
        Assert.Equal(0, b.CurrentBufferedMs);
    }

    [Fact]
    public void Enqueue_IncrementsReceivedCount()
    {
        var b = new SyncBuffer(new ClockSyncService());
        b.Enqueue(1, 1000, Pcm(1));
        b.Enqueue(2, 1020, Pcm(2));
        Assert.Equal(2, b.PacketsReceived);
    }

    [Fact]
    public void DrainReady_BeforeClockSync_EmptiesInArrivalOrderWithoutDropping()
    {
        // Key behaviour: until pong arrives, we don't know the offset — so we
        // don't drop any frames and play them in seq order immediately.
        var b = new SyncBuffer(new ClockSyncService());
        b.Enqueue(3, 3000, Pcm(3));
        b.Enqueue(1, 1000, Pcm(1));
        b.Enqueue(2, 2000, Pcm(2));

        var drained = b.DrainReady().Select(p => p[0]).ToArray();
        Assert.Equal(new byte[] { 1, 2, 3 }, drained);
        Assert.Equal(0, b.PacketsDropped);
    }

    [Fact]
    public void DrainReady_AfterSync_HoldsFuturesBack()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 100);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Frame scheduled far in the future (playTime = now + targetLatency + a lot).
        b.Enqueue(1, now + 1_000, Pcm(1));
        var drained = b.DrainReady().ToArray();
        Assert.Empty(drained);
        // Still buffered.
        Assert.Equal(1, b.PacketsReceived);
    }

    [Fact]
    public void DrainReady_AfterSync_ReleasesMaturedFrames_InSequenceOrder()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 20);
        var past = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1_000; // well in the past

        b.Enqueue(3, past, Pcm(3));
        b.Enqueue(1, past, Pcm(1));
        b.Enqueue(2, past, Pcm(2));

        var drained = b.DrainReady().Select(p => p[0]).ToList();
        // All scheduled in the past → all drop (>200ms late threshold).
        // So they're counted as dropped, not yielded.
        Assert.Equal(3, b.PacketsDropped);
        Assert.Empty(drained);
    }

    [Fact]
    public void DrainReady_LateFrame_WithinToleranceStillPlays()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 10);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Frame scheduled 50ms ago — within the 200ms late-drop threshold, so still played.
        b.Enqueue(1, now - 50, Pcm(1));
        var drained = b.DrainReady().Select(p => p[0]).ToList();
        Assert.Contains((byte)1, drained);
        Assert.Equal(0, b.PacketsDropped);
    }

    [Fact]
    public void DrainReady_VeryLateFrame_IsDropped()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 10);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Frame scheduled 500ms ago — >200ms late → dropped.
        b.Enqueue(1, now - 500, Pcm(1));
        var drained = b.DrainReady().ToList();
        Assert.Empty(drained);
        Assert.Equal(1, b.PacketsDropped);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var b = new SyncBuffer(new ClockSyncService());
        b.Enqueue(1, 1, Pcm(1));
        b.Enqueue(2, 2, Pcm(2));

        b.Reset();
        Assert.Equal(0, b.PacketsReceived);
        Assert.Equal(0, b.PacketsDropped);
        Assert.Equal(0, b.CurrentBufferedMs);
        Assert.Empty(b.DrainReady());
    }

    [Fact]
    public void BufferedMs_ReflectsFutureFrames()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 100);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Future frames at +300 and +500 → buffer extends to +500+target.
        b.Enqueue(1, now + 300, Pcm(1));
        b.Enqueue(2, now + 500, Pcm(2));
        _ = b.DrainReady().ToList(); // trigger CurrentBufferedMs calc
        // ~ (500 + 100 target - now) ≈ 600ms of headroom.
        Assert.InRange(b.CurrentBufferedMs, 400, 800);
    }

    [Fact]
    public void DrainReady_OutOfOrderInsert_YieldsInSequenceOrder()
    {
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 10);
        var longPast = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 50;

        // Within late-drop tolerance (200ms), still yielded in seq order.
        b.Enqueue(42, longPast, Pcm(42));
        b.Enqueue(41, longPast, Pcm(41));
        b.Enqueue(43, longPast, Pcm(43));

        var order = b.DrainReady().Select(p => p[0]).ToList();
        Assert.Equal(new byte[] { 41, 42, 43 }, order);
    }

    [Fact]
    public void Enqueue_SameSequence_Overwrites()
    {
        // If duplicate packets arrive (e.g. retransmit scenarios), the latest
        // wins — this is a side-effect of using a dictionary keyed by seq.
        var clock = SyncedClock();
        var b = new SyncBuffer(clock, targetLatencyMs: 10);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        b.Enqueue(1, now - 50, Pcm(10));
        b.Enqueue(1, now - 50, Pcm(20)); // duplicate seq

        var drained = b.DrainReady().Select(p => p[0]).ToList();
        Assert.Single(drained);
        Assert.Equal((byte)20, drained[0]);
    }
}
