using LiveStreamSound.Client.Services;

namespace LiveStreamSound.Client.Tests;

/// <summary>
/// Verifies the simplified NTP-like offset calculation used for sync playback.
/// Scenario shorthand: the client sent ping at T1, server recorded it at T2,
/// the pong got back at T3; we treat the one-way delay as RTT/2 and expect
/// (server + offset) ≈ client at any given moment.
/// </summary>
public class ClockSyncServiceTests
{
    [Fact]
    public void Initial_IsNotSynced()
    {
        var c = new ClockSyncService();
        Assert.False(c.IsSynced);
        Assert.Equal(0, c.OffsetMs);
        Assert.Equal(0, c.LastRttMs);
    }

    [Fact]
    public void NotifyPong_FirstSample_SetsSynced()
    {
        var c = new ClockSyncService();
        // Client sent ping at t=1000 (client clock).
        // Server pong says server clock was 2000 at pong time.
        // Client receives pong at t=1100 (client clock). RTT = 100.
        c.NotifyPong(clientTimeMs: 1000, serverTimeMs: 2000, nowMs: 1100);
        Assert.True(c.IsSynced);
        Assert.Equal(100, c.LastRttMs);
    }

    [Fact]
    public void NotifyPong_OffsetCalculation_MatchesOneWayDelayModel()
    {
        var c = new ClockSyncService();
        // client t=1000, server t=2000, pong back at 1100 → rtt=100, oneWay=50.
        // offset = (clientTimeMs + oneWay) - serverTimeMs = (1000+50) - 2000 = -950.
        c.NotifyPong(1000, 2000, 1100);
        Assert.Equal(-950, c.OffsetMs);
    }

    [Fact]
    public void NotifyPong_KeepsBestRttSample_IgnoresWorseOnes()
    {
        var c = new ClockSyncService();
        // First sample with 200ms RTT.
        c.NotifyPong(1000, 2000, 1200);
        var offsetAfterFirst = c.OffsetMs;

        // Second sample with a *worse* RTT (500ms) — should keep the first offset.
        c.NotifyPong(5000, 6000, 5500);
        Assert.Equal(offsetAfterFirst, c.OffsetMs);
    }

    [Fact]
    public void NotifyPong_BetterRtt_UpdatesOffset()
    {
        var c = new ClockSyncService();
        // First sample at RTT=500.
        c.NotifyPong(1000, 2000, 1500);
        var offsetOld = c.OffsetMs;

        // Better sample at RTT=50 — should take over.
        c.NotifyPong(5000, 6100, 5050);
        Assert.NotEqual(offsetOld, c.OffsetMs);

        // New offset = (5000+25) - 6100 = -1075
        Assert.Equal(-1075, c.OffsetMs);
    }

    [Fact]
    public void ServerToLocal_AppliesOffset()
    {
        var c = new ClockSyncService();
        c.NotifyPong(1000, 2000, 1100); // offset = -950
        Assert.Equal(2000 + (-950), c.ServerToLocal(2000));
        Assert.Equal(3000 + (-950), c.ServerToLocal(3000));
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var c = new ClockSyncService();
        c.NotifyPong(1000, 2000, 1100);
        Assert.True(c.IsSynced);

        c.Reset();
        Assert.False(c.IsSynced);
        Assert.Equal(0, c.OffsetMs);
        Assert.Equal(0, c.LastRttMs);
    }

    [Fact]
    public void ResetThenResync_ForgetsOldSamples()
    {
        var c = new ClockSyncService();
        // Very-low-RTT first sample — sticky unless we reset.
        c.NotifyPong(1000, 2000, 1010);
        var offsetBefore = c.OffsetMs;

        c.Reset();

        // Much worse new sample should now take over since we're back to default.
        c.NotifyPong(5000, 6000, 5500);
        Assert.NotEqual(offsetBefore, c.OffsetMs);
    }

    [Fact]
    public void ServerToLocal_BeforeAnySync_ReturnsInput()
    {
        var c = new ClockSyncService();
        Assert.Equal(12345, c.ServerToLocal(12345));
    }

    [Fact]
    public async Task ConcurrentPongs_AreThreadSafe()
    {
        var c = new ClockSyncService();
        var tasks = Enumerable.Range(0, 32)
            .Select(i => Task.Run(() =>
            {
                for (var j = 0; j < 500; j++)
                    c.NotifyPong(j + i, j + 100 + i, j + 50 + i);
            }))
            .ToArray();
        await Task.WhenAll(tasks);
        // Doesn't throw / deadlock.
        Assert.True(c.IsSynced);
    }

    [Fact]
    public void NotifyPong_IdenticalBestRtt_TwiceDoesNotRevert()
    {
        var c = new ClockSyncService();
        c.NotifyPong(1000, 2000, 1100); // rtt=100
        var offset1 = c.OffsetMs;
        c.NotifyPong(3000, 4000, 3100); // same rtt=100 → offset recomputed identically? no — only strictly better
        // strictly better check is `rtt < _bestRttMs`, so equal RTT is skipped. Offset stable.
        Assert.Equal(offset1, c.OffsetMs);
    }
}
