using System.Collections.Concurrent;
using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Derives per-client <see cref="ConnectionQuality"/> from collected metrics.
/// Runs periodically; flags issues (no audio, high latency, stale status, etc.).
/// The UI translates each <see cref="ConnectionIssue"/> to a localized description.
/// </summary>
public sealed class DiagnosticsService : IDisposable
{
    private readonly SessionManager _sessions;
    private readonly AudioPipelineState _pipeline;
    private readonly LogService _log;
    private readonly Timer _timer;
    private readonly ConcurrentQueue<uint> _recentSequence = new();

    public event Action<ConnectedClient>? ClientQualityUpdated;

    public DiagnosticsService(SessionManager sessions, AudioPipelineState pipeline, LogService log)
    {
        _sessions = sessions;
        _pipeline = pipeline;
        _log = log;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Tick()
    {
        foreach (var client in _sessions.Clients)
        {
            var issues = new List<ConnectionIssue>();
            var now = DateTimeOffset.Now;

            var staleSeconds = (now - client.LastStatusReceived).TotalSeconds;
            if (staleSeconds > 10)
                issues.Add(ConnectionIssue.ClientNotResponding);

            if (client.LastRoundTripMs > 150)
                issues.Add(ConnectionIssue.HighLatency);

            if (client.LastPacketLossPercent > 2)
                issues.Add(ConnectionIssue.PacketLoss);

            if (client.LastJitterMs > 30)
                issues.Add(ConnectionIssue.ClockDriftHigh);

            if (client.LastBufferedMs > 0 && client.LastBufferedMs < 20)
                issues.Add(ConnectionIssue.BufferUnderrun);

            if (!_pipeline.AudioFlowing && client.AudioEndpoint is not null)
                issues.Add(ConnectionIssue.NoAudioStreamOnHost);

            var level = Classify(issues, staleSeconds);
            client.CurrentQuality = new ConnectionQuality(
                client.LastRoundTripMs,
                client.LastPacketLossPercent,
                client.LastJitterMs,
                client.LastBufferedMs,
                issues,
                level);
            ClientQualityUpdated?.Invoke(client);
        }
    }

    private static QualityLevel Classify(List<ConnectionIssue> issues, double staleSeconds)
    {
        if (issues.Contains(ConnectionIssue.ClientNotResponding) || staleSeconds > 30)
            return QualityLevel.Disconnected;
        if (issues.Contains(ConnectionIssue.PacketLoss) || issues.Contains(ConnectionIssue.BufferUnderrun))
            return QualityLevel.Bad;
        if (issues.Count > 0)
            return QualityLevel.Degraded;
        return QualityLevel.Good;
    }

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// Tracks whether the host pipeline is currently producing audio (non-silence frames).
/// </summary>
public sealed class AudioPipelineState
{
    private DateTimeOffset _lastNonSilenceFrame = DateTimeOffset.MinValue;

    public void NotifyFrame(ReadOnlySpan<byte> pcm)
    {
        // Quick silence heuristic: if all samples are near-zero, treat as silence.
        var hasSignal = false;
        for (var i = 0; i < pcm.Length; i += 32)
        {
            var sample = (short)(pcm[i] | (pcm[Math.Min(i + 1, pcm.Length - 1)] << 8));
            if (Math.Abs((int)sample) > 300) { hasSignal = true; break; }
        }
        if (hasSignal) _lastNonSilenceFrame = DateTimeOffset.Now;
    }

    public bool AudioFlowing =>
        (DateTimeOffset.Now - _lastNonSilenceFrame).TotalSeconds < 3;
}
