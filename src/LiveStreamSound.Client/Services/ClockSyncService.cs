namespace LiveStreamSound.Client.Services;

/// <summary>
/// Tracks the offset between the host's clock and the local clock using a simplified
/// NTP-like scheme on top of PING/PONG control messages.
///
/// For a ping: client sends T1 (local), server replies with T2 (server) at receipt.
/// After RTT is known, assume one-way delay = RTT/2, so serverNow ≈ clientNow - offset.
/// We keep the minimum-RTT sample as the best estimate.
/// </summary>
public sealed class ClockSyncService
{
    /// <summary>
    /// Maximum plausible offset between host and client clocks. Anything beyond
    /// suggests a CMOS-battery-dead host or a freshly-imaged lab PC with default
    /// 2010 system date — not a real network-induced offset. We refuse to apply
    /// such an offset and surface a SystemClockSuspect issue to the UI.
    /// </summary>
    public const long MaxPlausibleOffsetMs = 24L * 60 * 60 * 1000; // ±1 day

    private long _bestRttMs = long.MaxValue;
    private long _offsetMs;
    private bool _isSynced;
    private bool _clockSuspect;
    private readonly object _lock = new();

    public long OffsetMs { get { lock (_lock) return _offsetMs; } }
    public long LastRttMs { get; private set; }
    public bool IsSynced { get { lock (_lock) return _isSynced; } }

    /// <summary>True if we've seen a pong whose computed offset exceeded
    /// <see cref="MaxPlausibleOffsetMs"/> — indicating a bad system clock.</summary>
    public bool ClockSuspect { get { lock (_lock) return _clockSuspect; } }

    public void NotifyPong(long clientTimeMs, long serverTimeMs, long nowMs)
    {
        var rtt = nowMs - clientTimeMs;
        LastRttMs = rtt;
        lock (_lock)
        {
            if (rtt < _bestRttMs)
            {
                // offset such that serverTimeMs + offset ≈ clientNowMs when packet was mid-flight
                var oneWay = rtt / 2;
                var newOffset = (clientTimeMs + oneWay) - serverTimeMs;
                if (Math.Abs(newOffset) > MaxPlausibleOffsetMs)
                {
                    // Don't apply nonsense offsets. Audio sync degrades to
                    // no-offset (frames played in arrival order), which still
                    // produces *audible* sound — better than silent nothing
                    // due to "frame is in the year 2026 / 2010 future".
                    _clockSuspect = true;
                }
                else
                {
                    _bestRttMs = rtt;
                    _offsetMs = newOffset;
                }
            }
            _isSynced = true;
        }
    }

    /// <summary>Converts a server timestamp (ms) to local time (ms).</summary>
    public long ServerToLocal(long serverTimeMs)
    {
        lock (_lock) return serverTimeMs + _offsetMs;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _bestRttMs = long.MaxValue;
            _offsetMs = 0;
            _isSynced = false;
            _clockSuspect = false;
        }
        LastRttMs = 0;
    }
}
