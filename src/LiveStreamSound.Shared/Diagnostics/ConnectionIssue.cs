namespace LiveStreamSound.Shared.Diagnostics;

/// <summary>
/// Machine-readable connection problems. UI translates each to a localized, teacher-friendly
/// description via its resx resources (see Host/Client Resources/Strings.resx).
/// </summary>
public enum ConnectionIssue
{
    None,

    /// <summary>Host is connected but no audio is flowing (nothing playing in VLC/browser etc.).</summary>
    NoAudioStreamOnHost,

    /// <summary>Round-trip time is higher than expected — usually weak Wi-Fi or congested network.</summary>
    HighLatency,

    /// <summary>Audio packets are being dropped — Wi-Fi interference or bandwidth saturation.</summary>
    PacketLoss,

    /// <summary>Client's jitter buffer is empty — data arrives too slowly.</summary>
    BufferUnderrun,

    /// <summary>Clock drift between host and client above tolerance — slight re-sync may occur.</summary>
    ClockDriftHigh,

    /// <summary>TCP control channel lost — client is no longer reachable.</summary>
    Disconnected,

    /// <summary>Wi-Fi AP isolates clients (common in school networks) — mDNS discovery blocked.</summary>
    NetworkIsolation,

    /// <summary>UDP packets blocked, likely Windows Firewall or network policy.</summary>
    FirewallUdpBlocked,

    /// <summary>Reported signal quality on client is poor.</summary>
    WlanSignalWeak,

    /// <summary>Client hasn't reported status in too long.</summary>
    ClientNotResponding,
}

public enum QualityLevel
{
    Disconnected = 0,
    Bad = 1,
    Degraded = 2,
    Good = 3,
}

public record ConnectionQuality(
    int RoundTripMs,
    float PacketLossPercent,
    int JitterMs,
    int BufferedMs,
    IReadOnlyList<ConnectionIssue> ActiveIssues,
    QualityLevel Level);
