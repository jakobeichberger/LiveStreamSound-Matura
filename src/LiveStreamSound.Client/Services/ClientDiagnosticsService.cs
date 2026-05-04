using LiveStreamSound.Shared.Diagnostics;

namespace LiveStreamSound.Client.Services;

/// <summary>
/// Derives the client's own connection-quality summary from its pipeline metrics.
/// Runs on a 1-second tick and raises <see cref="QualityChanged"/> whenever the level changes.
/// </summary>
public sealed class ClientDiagnosticsService : IDisposable
{
    private readonly ControlClient _control;
    private readonly AudioStreamClient _audio;
    private readonly SyncBuffer _buffer;
    private readonly ClockSyncService _clockSync;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _tickTask;
    public ConnectionQuality Current { get; private set; } =
        new(0, 0, 0, 0, Array.Empty<ConnectionIssue>(), QualityLevel.Disconnected);
    public event Action<ConnectionQuality>? QualityChanged;

    private int _lastReceivedFrames;
    private int _lastLostFrames;
    private DateTimeOffset _lastTick = DateTimeOffset.Now;
    private DateTimeOffset _lastPacketSeen = DateTimeOffset.MinValue;

    public ClientDiagnosticsService(
        ControlClient control,
        AudioStreamClient audio,
        SyncBuffer buffer,
        ClockSyncService clockSync)
    {
        _control = control;
        _audio = audio;
        _buffer = buffer;
        _clockSync = clockSync;
        // PeriodicTimer + serial loop — Tick() can never re-enter itself.
        _tickTask = Task.Run(() => RunTickLoopAsync(_cts.Token));
    }

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        using var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await t.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try { Tick(); }
                catch { /* never throw from a Tick */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Tick()
    {
        var issues = new List<ConnectionIssue>();
        var now = DateTimeOffset.Now;

        if (_control.State == ControlClientState.Disconnected ||
            _control.State == ControlClientState.Failed)
        {
            issues.Add(ConnectionIssue.Disconnected);
        }

        var newReceived = _audio.ReceivedFrames;
        var newLost = _audio.LostFrames;
        var deltaReceived = newReceived - _lastReceivedFrames;
        var deltaLost = newLost - _lastLostFrames;
        var totalExpected = deltaReceived + deltaLost;
        var lossPct = totalExpected == 0 ? 0f : 100f * deltaLost / totalExpected;
        _lastReceivedFrames = newReceived;
        _lastLostFrames = newLost;

        if (deltaReceived > 0) _lastPacketSeen = now;
        var secondsSinceAudio = (now - _lastPacketSeen).TotalSeconds;

        if (lossPct > 2) issues.Add(ConnectionIssue.PacketLoss);

        var rtt = (int)_clockSync.LastRttMs;
        if (rtt > 150) issues.Add(ConnectionIssue.HighLatency);

        if (_control.State == ControlClientState.Connected &&
            _audio.Port > 0 &&
            secondsSinceAudio > 3)
            issues.Add(ConnectionIssue.FirewallUdpBlocked);

        if (_control.State == ControlClientState.Connected &&
            deltaReceived > 0 &&
            _buffer.CurrentBufferedMs < 20)
            issues.Add(ConnectionIssue.BufferUnderrun);

        if (_control.State == ControlClientState.Connected &&
            deltaReceived == 0 &&
            secondsSinceAudio > 5 &&
            !issues.Contains(ConnectionIssue.FirewallUdpBlocked))
            issues.Add(ConnectionIssue.NoAudioStreamOnHost);

        // Surface system-clock weirdness as a UI hint — sync is degraded but
        // audio still plays in arrival-order, so it's a "yellow" issue.
        if (_clockSync.ClockSuspect)
            issues.Add(ConnectionIssue.SystemClockSuspect);

        var level = Classify(_control.State, issues);
        var q = new ConnectionQuality(rtt, lossPct, 0, _buffer.CurrentBufferedMs, issues, level);
        Current = q;
        QualityChanged?.Invoke(q);
    }

    private static QualityLevel Classify(ControlClientState state, List<ConnectionIssue> issues)
    {
        if (state != ControlClientState.Connected) return QualityLevel.Disconnected;
        if (issues.Contains(ConnectionIssue.PacketLoss) ||
            issues.Contains(ConnectionIssue.BufferUnderrun) ||
            issues.Contains(ConnectionIssue.FirewallUdpBlocked))
            return QualityLevel.Bad;
        if (issues.Count > 0) return QualityLevel.Degraded;
        return QualityLevel.Good;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _tickTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
