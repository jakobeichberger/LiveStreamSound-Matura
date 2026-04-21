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
    private long _bestRttMs = long.MaxValue;
    private long _offsetMs;
    private bool _isSynced;
    private readonly object _lock = new();

    public long OffsetMs { get { lock (_lock) return _offsetMs; } }
    public long LastRttMs { get; private set; }
    public bool IsSynced { get { lock (_lock) return _isSynced; } }

    public void NotifyPong(long clientTimeMs, long serverTimeMs, long nowMs)
    {
        var rtt = nowMs - clientTimeMs;
        LastRttMs = rtt;
        lock (_lock)
        {
            if (rtt < _bestRttMs)
            {
                _bestRttMs = rtt;
                // offset such that serverTimeMs + offset ≈ clientNowMs when packet was mid-flight
                var oneWay = rtt / 2;
                _offsetMs = (clientTimeMs + oneWay) - serverTimeMs;
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
        }
        LastRttMs = 0;
    }
}
