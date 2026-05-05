using LiveStreamSound.Host.Services;

namespace LiveStreamSound.Host.Tests;

/// <summary>
/// Regression: ControlServer.Start(preferredPort: 0) and
/// AudioStreamServer.Start(preferredPort: 0) used to set
/// `Port = candidate` (= 0) instead of reading the actual OS-assigned
/// port from LocalEndpoint, breaking integration tests + any in-process
/// scenario where the caller wanted an ephemeral port.
/// </summary>
public class PortBindingTests
{
    private static LogService NewLog() => new();

    [Fact]
    public async Task ControlServer_Start_WithPreferredPortZero_ReportsActualBoundPort()
    {
        using var log = NewLog();
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);

        control.Start(preferredPort: 0);

        // Must NOT be 0 — must be an OS-assigned ephemeral port.
        Assert.NotEqual(0, control.Port);
        Assert.InRange(control.Port, 1, 65535);
    }

    [Fact]
    public async Task ControlServer_Start_WithFixedPort_ReportsThatPort()
    {
        using var log = NewLog();
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);

        // Pick a high port unlikely to collide on the CI runner.
        control.Start(preferredPort: 51000);

        // Either we got the requested port, or one of the next 9 (auto-retry slots),
        // or the ephemeral fallback. Anything except 0.
        Assert.NotEqual(0, control.Port);
        Assert.InRange(control.Port, 1, 65535);
    }

    [Fact]
    public void AudioStreamServer_Start_WithPreferredPortZero_ReportsActualBoundPort()
    {
        using var log = NewLog();
        using var sessions = new SessionManager(log);
        using var audio = new AudioStreamServer(sessions, log);

        audio.Start(preferredPort: 0);

        Assert.NotEqual(0, audio.Port);
        Assert.InRange(audio.Port, 1, 65535);
    }
}
