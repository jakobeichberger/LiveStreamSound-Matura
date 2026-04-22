using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiveStreamSound.Shared.Diagnostics;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Host.Services;

public sealed class ConnectedClient
{
    public required string ClientId { get; init; }
    public required string ClientName { get; init; }
    // TcpClient + TcpEndpoint are re-assignable so a rejoining client can slot
    // its fresh TCP connection into the existing model object (preserves ClientId
    // + volume/mute/device settings across a WLAN hiccup).
    public required TcpClient TcpClient { get; set; }
    public required IPEndPoint TcpEndpoint { get; set; }

    public IPEndPoint? AudioEndpoint { get; set; }
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public string? CurrentOutputDeviceId { get; set; }

    public int LastRoundTripMs { get; set; }
    public float LastPacketLossPercent { get; set; }
    public int LastJitterMs { get; set; }
    public int LastBufferedMs { get; set; }
    public DateTimeOffset LastStatusReceived { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.Now;
    public ConnectionQuality CurrentQuality { get; set; } =
        new(0, 0, 0, 0, Array.Empty<ConnectionIssue>(), QualityLevel.Good);

    /// <summary>
    /// Set to true when the control channel drops but we're still inside the
    /// rejoin grace period. Audio broadcasts skip reconnecting clients so we
    /// don't spam their stale UDP endpoint.
    /// </summary>
    public bool IsReconnecting { get; set; }

    /// <summary>Timestamp of the last UnregisterClient call that flipped IsReconnecting on.</summary>
    public DateTimeOffset? ReconnectingSince { get; set; }

    /// <summary>Serializes writes to the TCP stream so concurrent sends from UI + read-loop don't interleave.</summary>
    public SemaphoreSlim WriteLock { get; set; } = new(1, 1);
}

public sealed class SessionManager : IDisposable
{
    private readonly LogService _log;
    public string? Code { get; private set; }
    public Guid SessionId { get; private set; }
    public bool IsActive => Code is not null;
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>A genuinely new client is connecting for the first time in this session.</summary>
    public event Action<ConnectedClient>? ClientJoined;
    /// <summary>The client's TCP channel just dropped but the grace period is active —
    /// the tile should stay visible in a "reconnecting" state until either
    /// <see cref="ClientRejoined"/> or <see cref="ClientLeft"/> fires.</summary>
    public event Action<ConnectedClient>? ClientReconnecting;
    /// <summary>A recently-disconnected client re-established its TCP within the grace period.
    /// State (volume, mute, device) has been preserved. The ClientId matches the pre-drop one.</summary>
    public event Action<ConnectedClient>? ClientRejoined;
    /// <summary>The client is finally gone — either grace expired without rejoin, or we kicked it.</summary>
    public event Action<ConnectedClient>? ClientLeft;
    public event Action? SessionStateChanged;

    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly Timer _sweepTimer;

    /// <summary>
    /// How long a disconnected client's state is preserved for a possible rejoin.
    /// Beyond this, the grace period lapses and the client is fully removed.
    /// </summary>
    public TimeSpan RejoinGracePeriod { get; set; } = TimeSpan.FromSeconds(60);

    public SessionManager(LogService log)
    {
        _log = log;
        _sweepTimer = new Timer(_ => SweepExpiredReconnects(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public IReadOnlyCollection<ConnectedClient> Clients => _clients.Values.ToArray();

    /// <summary>Active (non-reconnecting) clients only — use this for audio broadcast.</summary>
    public IEnumerable<ConnectedClient> ActiveClients => _clients.Values.Where(c => !c.IsReconnecting);

    public string StartSession()
    {
        Code = SessionCode.Generate();
        SessionId = Guid.NewGuid();
        StartedAt = DateTimeOffset.Now;
        // Deliberately not logging the code — file logs shouldn't contain the secret.
        // The UI shows it plainly and the client gets it via HELLO anyway.
        _log.Info("Session", $"Session started, id={SessionId}");
        SessionStateChanged?.Invoke();
        return Code;
    }

    public void StopSession()
    {
        _log.Info("Session", $"Session stopping (had {_clients.Count} clients)");
        foreach (var client in _clients.Values)
        {
            try { client.TcpClient.Close(); } catch { }
        }
        _clients.Clear();
        Code = null;
        StartedAt = null;
        SessionStateChanged?.Invoke();
    }

    public bool ValidateCode(string code) =>
        IsActive && string.Equals(code, Code, StringComparison.Ordinal);

    /// <summary>
    /// Check whether a HELLO from <paramref name="clientName"/> matches a
    /// recently-disconnected client. If so, return the existing model (caller
    /// should slot in the new TCP connection and call <see cref="FinalizeRejoin"/>).
    /// </summary>
    public ConnectedClient? TryFindReconnectingByName(string clientName)
    {
        var normalized = NormalizeName(clientName);
        return _clients.Values.FirstOrDefault(c =>
            c.IsReconnecting && NormalizeName(c.ClientName) == normalized);
    }

    /// <summary>
    /// Called after a rejoin match — caller has already updated TcpClient /
    /// TcpEndpoint / WriteLock with the new connection. Clears the reconnecting
    /// flag and fires <see cref="ClientRejoined"/>.
    /// </summary>
    public void FinalizeRejoin(ConnectedClient client)
    {
        client.IsReconnecting = false;
        client.ReconnectingSince = null;
        _log.Info("Session",
            $"Client rejoined within grace period: {client.ClientName} ({client.ClientId}), " +
            $"volume={client.Volume:F2} muted={client.IsMuted}");
        ClientRejoined?.Invoke(client);
    }

    public ConnectedClient RegisterClient(ConnectedClient client)
    {
        _clients[client.ClientId] = client;
        _log.Info("Session", $"Client joined: {client.ClientName} ({client.ClientId}) from {client.TcpEndpoint}");
        ClientJoined?.Invoke(client);
        return client;
    }

    /// <summary>
    /// Soft-unregister: marks the client as reconnecting and keeps it in
    /// <see cref="Clients"/> for <see cref="RejoinGracePeriod"/>. UI tile stays
    /// visible in a degraded state; a <see cref="ClientRejoined"/> within that
    /// window fully restores it. Use <see cref="HardUnregisterClient"/> to skip
    /// grace (e.g. for explicit kicks).
    /// </summary>
    public void UnregisterClient(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            if (client.IsReconnecting) return; // already in grace period
            client.IsReconnecting = true;
            client.ReconnectingSince = DateTimeOffset.UtcNow;
            try { client.TcpClient.Close(); } catch { }
            _log.Info("Session",
                $"Client disconnected (grace period active): {client.ClientName} ({clientId})");
            ClientReconnecting?.Invoke(client);
        }
    }

    /// <summary>Hard-remove without grace period. Used for kicks and session-end.</summary>
    public void HardUnregisterClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            _log.Info("Session", $"Client left (hard unregister): {client.ClientName} ({clientId})");
            try { client.TcpClient.Close(); } catch { }
            ClientLeft?.Invoke(client);
        }
    }

    private void SweepExpiredReconnects()
    {
        var cutoff = DateTimeOffset.UtcNow - RejoinGracePeriod;
        foreach (var client in _clients.Values.ToArray())
        {
            if (!client.IsReconnecting) continue;
            if (client.ReconnectingSince is null) continue;
            if (client.ReconnectingSince < cutoff)
            {
                if (_clients.TryRemove(client.ClientId, out var removed))
                {
                    _log.Info("Session",
                        $"Grace period expired for {removed.ClientName} — removing for good");
                    ClientLeft?.Invoke(removed);
                }
            }
        }
    }

    public ConnectedClient? GetClient(string clientId) =>
        _clients.TryGetValue(clientId, out var c) ? c : null;

    private static string NormalizeName(string name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();

    public void Dispose()
    {
        try { _sweepTimer.Dispose(); } catch { }
    }
}
