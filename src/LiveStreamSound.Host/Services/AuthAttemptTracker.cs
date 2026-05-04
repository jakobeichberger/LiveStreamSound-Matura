using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Per-prefix sliding window of failed HELLO attempts, so a remote cannot
/// brute-force the 6-digit session code.
///
/// <para>
/// Keys are <b>prefixes</b> (IPv4 /24 = first 3 octets, IPv6 /64 = first 8
/// bytes), not exact addresses, so an attacker rotating IPs within the same
/// subnet doesn't get fresh budgets. On a school /24 this caps brute-force
/// at 5 attempts/minute total across the entire LAN segment. IPv6 link-local
/// privacy-extension rotation is similarly bounded since all addresses share
/// the same /64.
/// </para>
///
/// <para>
/// Dictionary is bounded — at <see cref="MaxTrackedPrefixes"/> entries we
/// evict the oldest. Defends against memory-exhaustion DoS via spoofed
/// source IPs (CWE-770).
/// </para>
/// </summary>
public sealed class AuthAttemptTracker
{
    public const int MaxTrackedPrefixes = 4096;

    private readonly int _maxFailuresPerWindow;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, IpState> _perPrefix = new();

    public AuthAttemptTracker(int maxFailuresPerWindow = 5, TimeSpan? window = null)
    {
        _maxFailuresPerWindow = maxFailuresPerWindow;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>True if the given IP is allowed to attempt HELLO right now.</summary>
    public bool AllowAttempt(IPAddress remote)
    {
        var key = PrefixKey(remote);
        var state = GetOrAdd(key);
        lock (state.SyncRoot)
        {
            TrimOldFailures(state);
            return state.RecentFailures.Count < _maxFailuresPerWindow;
        }
    }

    /// <summary>Record a failed HELLO (invalid code / stale session).</summary>
    public void RecordFailure(IPAddress remote)
    {
        var key = PrefixKey(remote);
        var state = GetOrAdd(key);
        lock (state.SyncRoot)
        {
            state.RecentFailures.Add(DateTimeOffset.UtcNow);
            state.LastSeenUtc = DateTimeOffset.UtcNow;
            TrimOldFailures(state);
        }
    }

    /// <summary>Reset counter on a successful HELLO so the prefix isn't stuck cooling down.</summary>
    public void RecordSuccess(IPAddress remote)
    {
        var key = PrefixKey(remote);
        if (_perPrefix.TryGetValue(key, out var state))
        {
            lock (state.SyncRoot)
            {
                state.RecentFailures.Clear();
                state.LastSeenUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>How many failures are still inside the rate-limit window for the given source's prefix.</summary>
    public int CurrentFailureCount(IPAddress remote)
    {
        var key = PrefixKey(remote);
        if (!_perPrefix.TryGetValue(key, out var state)) return 0;
        lock (state.SyncRoot)
        {
            TrimOldFailures(state);
            return state.RecentFailures.Count;
        }
    }

    /// <summary>For tests / diagnostics.</summary>
    public int TrackedPrefixCount => _perPrefix.Count;

    /// <summary>
    /// Reduce a source <see cref="IPAddress"/> to its bucket key. IPv4 → /24;
    /// IPv6 → /64. ISP / corporate NAT and Wi-Fi network-private subnets all
    /// get a single shared budget — desired for our threat model (any client
    /// on the same school LAN segment is potentially the attacker).
    /// </summary>
    public static string PrefixKey(IPAddress addr)
    {
        var bytes = addr.GetAddressBytes();
        if (addr.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return $"v4:{bytes[0]}.{bytes[1]}.{bytes[2]}";
        }
        if (addr.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            // First 8 bytes = /64 prefix.
            return "v6:" + Convert.ToHexString(bytes, 0, 8);
        }
        return "raw:" + addr;
    }

    private IpState GetOrAdd(string key)
    {
        var state = _perPrefix.GetOrAdd(key, _ => new IpState());
        // Bounded dictionary defense: if we're over capacity, evict the
        // oldest-LastSeenUtc entry. Trim is best-effort (concurrent races
        // OK — dictionary just stays a bit over cap briefly).
        if (_perPrefix.Count > MaxTrackedPrefixes)
            EvictOldest();
        return state;
    }

    private void EvictOldest()
    {
        try
        {
            var snapshot = _perPrefix.ToArray();
            var oldest = snapshot
                .OrderBy(kv => kv.Value.LastSeenUtc)
                .Take(Math.Max(1, snapshot.Length / 8))
                .ToList();
            foreach (var kv in oldest)
                _perPrefix.TryRemove(kv);
        }
        catch { /* best-effort eviction — never throw */ }
    }

    private void TrimOldFailures(IpState state)
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        state.RecentFailures.RemoveAll(t => t < cutoff);
    }

    private sealed class IpState
    {
        public readonly object SyncRoot = new();
        public readonly List<DateTimeOffset> RecentFailures = new();
        public DateTimeOffset LastSeenUtc = DateTimeOffset.UtcNow;
    }
}
