using System.Collections.Concurrent;
using System.Net;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Per-source-IP sliding window of failed HELLO attempts, so a remote cannot
/// brute-force the 6-digit session code (~1M combinations — practical at 1000
/// req/s without a rate limit). Default policy: max 5 failures per minute.
/// </summary>
public sealed class AuthAttemptTracker
{
    private readonly int _maxFailuresPerWindow;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<IPAddress, IpState> _perIp = new();

    public AuthAttemptTracker(int maxFailuresPerWindow = 5, TimeSpan? window = null)
    {
        _maxFailuresPerWindow = maxFailuresPerWindow;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>True if the given IP is allowed to attempt HELLO right now.</summary>
    public bool AllowAttempt(IPAddress remote)
    {
        var state = _perIp.GetOrAdd(remote, _ => new IpState());
        lock (state.SyncRoot)
        {
            TrimOldFailures(state);
            return state.RecentFailures.Count < _maxFailuresPerWindow;
        }
    }

    /// <summary>Record a failed HELLO (invalid code / stale session).</summary>
    public void RecordFailure(IPAddress remote)
    {
        var state = _perIp.GetOrAdd(remote, _ => new IpState());
        lock (state.SyncRoot)
        {
            state.RecentFailures.Add(DateTimeOffset.UtcNow);
            TrimOldFailures(state);
        }
    }

    /// <summary>Reset counter on a successful HELLO so the IP isn't stuck cooling down.</summary>
    public void RecordSuccess(IPAddress remote)
    {
        if (_perIp.TryGetValue(remote, out var state))
        {
            lock (state.SyncRoot) state.RecentFailures.Clear();
        }
    }

    private void TrimOldFailures(IpState state)
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        state.RecentFailures.RemoveAll(t => t < cutoff);
    }

    public int CurrentFailureCount(IPAddress remote)
    {
        if (!_perIp.TryGetValue(remote, out var state)) return 0;
        lock (state.SyncRoot)
        {
            TrimOldFailures(state);
            return state.RecentFailures.Count;
        }
    }

    private sealed class IpState
    {
        public readonly object SyncRoot = new();
        public readonly List<DateTimeOffset> RecentFailures = new();
    }
}
