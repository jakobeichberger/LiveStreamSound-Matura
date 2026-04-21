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
    public required TcpClient TcpClient { get; init; }
    public required IPEndPoint TcpEndpoint { get; init; }

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

    /// <summary>Serializes writes to the TCP stream so concurrent sends from UI + read-loop don't interleave.</summary>
    public SemaphoreSlim WriteLock { get; } = new(1, 1);
}

public sealed class SessionManager
{
    private readonly LogService _log;
    public string? Code { get; private set; }
    public Guid SessionId { get; private set; }
    public bool IsActive => Code is not null;
    public DateTimeOffset? StartedAt { get; private set; }
    public event Action<ConnectedClient>? ClientJoined;
    public event Action<ConnectedClient>? ClientLeft;
    public event Action? SessionStateChanged;

    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();

    public SessionManager(LogService log) { _log = log; }

    public IReadOnlyCollection<ConnectedClient> Clients => _clients.Values.ToArray();

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

    public ConnectedClient RegisterClient(ConnectedClient client)
    {
        _clients[client.ClientId] = client;
        _log.Info("Session", $"Client joined: {client.ClientName} ({client.ClientId}) from {client.TcpEndpoint}");
        ClientJoined?.Invoke(client);
        return client;
    }

    public void UnregisterClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            _log.Info("Session", $"Client left: {client.ClientName} ({client.ClientId})");
            try { client.TcpClient.Close(); } catch { }
            ClientLeft?.Invoke(client);
        }
    }

    public ConnectedClient? GetClient(string clientId) =>
        _clients.TryGetValue(clientId, out var c) ? c : null;
}
