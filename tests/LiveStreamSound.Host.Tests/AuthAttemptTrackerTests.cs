using System.Net;
using LiveStreamSound.Host.Services;

namespace LiveStreamSound.Host.Tests;

/// <summary>
/// The rate-limiter sits between the public internet (well, LAN) and the
/// HELLO handshake. Brute-forcing the 6-digit session code is the threat
/// model it blocks. Tests cover: threshold trigger, per-IP isolation,
/// window expiry, success-resets-counter, concurrent pounding.
/// </summary>
public class AuthAttemptTrackerTests
{
    private static readonly IPAddress Ip1 = IPAddress.Parse("192.168.1.100");
    private static readonly IPAddress Ip2 = IPAddress.Parse("192.168.1.101");

    [Fact]
    public void AllowsFirstAttempts_UpToThreshold()
    {
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 5);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(t.AllowAttempt(Ip1), $"attempt #{i + 1} should be allowed");
            t.RecordFailure(Ip1);
        }
        // 5 failures recorded → next call blocks.
        Assert.False(t.AllowAttempt(Ip1));
    }

    [Fact]
    public void BlockedAfterThreshold_ReleasesWhenWindowExpires()
    {
        // Tiny window so the test is fast.
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 3, window: TimeSpan.FromMilliseconds(120));
        for (var i = 0; i < 3; i++) t.RecordFailure(Ip1);
        Assert.False(t.AllowAttempt(Ip1));

        // Wait out the window.
        Thread.Sleep(200);
        Assert.True(t.AllowAttempt(Ip1));
        Assert.Equal(0, t.CurrentFailureCount(Ip1));
    }

    [Fact]
    public void PerIp_Isolation()
    {
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 2);
        t.RecordFailure(Ip1);
        t.RecordFailure(Ip1);
        Assert.False(t.AllowAttempt(Ip1));

        // Different IP still allowed — we don't want one misbehaving client
        // to DoS other students on the same LAN.
        Assert.True(t.AllowAttempt(Ip2));
    }

    [Fact]
    public void RecordSuccess_ClearsCounterImmediately()
    {
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 5);
        for (var i = 0; i < 4; i++) t.RecordFailure(Ip1);
        Assert.Equal(4, t.CurrentFailureCount(Ip1));

        t.RecordSuccess(Ip1);
        Assert.Equal(0, t.CurrentFailureCount(Ip1));
        Assert.True(t.AllowAttempt(Ip1));
    }

    [Fact]
    public void RecordSuccess_OnUnseenIp_IsSafe()
    {
        var t = new AuthAttemptTracker();
        t.RecordSuccess(Ip1); // should not throw
        Assert.Equal(0, t.CurrentFailureCount(Ip1));
    }

    [Fact]
    public async Task ConcurrentFailures_OneIp_AccountsCorrectly()
    {
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 1000);
        // Hammer the tracker from many threads.
        var tasks = Enumerable.Range(0, 40)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 25; i++) t.RecordFailure(Ip1);
            }))
            .ToArray();
        await Task.WhenAll(tasks);

        // Expect exactly 40 * 25 = 1000 entries — the ConcurrentDictionary +
        // lock combo should prevent lost increments.
        Assert.Equal(1000, t.CurrentFailureCount(Ip1));
    }

    [Fact]
    public void DefaultThreshold_BruteForceRateIsNotPractical()
    {
        // Document the default: 5 attempts/minute. 1M codes × 1min/5 =
        // 200,000 minutes ≈ 138 days of continuous pounding to exhaust space.
        var t = new AuthAttemptTracker(); // defaults
        for (var i = 0; i < 5; i++)
        {
            Assert.True(t.AllowAttempt(Ip1));
            t.RecordFailure(Ip1);
        }
        Assert.False(t.AllowAttempt(Ip1));
    }

    [Fact]
    public void CurrentFailureCount_UnknownIp_IsZero()
    {
        var t = new AuthAttemptTracker();
        Assert.Equal(0, t.CurrentFailureCount(IPAddress.Parse("10.0.0.99")));
    }

    [Fact]
    public void FailuresTrim_OnEveryCount_Read()
    {
        // TrimOldFailures runs lazily on AllowAttempt/CurrentFailureCount.
        var t = new AuthAttemptTracker(maxFailuresPerWindow: 10, window: TimeSpan.FromMilliseconds(80));
        for (var i = 0; i < 3; i++) t.RecordFailure(Ip1);
        Assert.Equal(3, t.CurrentFailureCount(Ip1));

        Thread.Sleep(150);
        Assert.Equal(0, t.CurrentFailureCount(Ip1));
    }
}
