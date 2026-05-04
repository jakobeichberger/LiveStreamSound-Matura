using System.Runtime.InteropServices;

namespace LiveStreamSound.App.Services;

/// <summary>
/// Tells Windows that the system + display should not auto-sleep while a
/// LiveStreamSound session is active. Without this, a teacher's laptop with
/// "lid close → sleep" set will hibernate during a 60-minute exam if the
/// teacher walks to the back of the room. Same for client laptops that have
/// been sitting idle since 08:00 setup.
/// <para>
/// Win32 SetThreadExecutionState with ES_CONTINUOUS keeps the request active
/// until we explicitly clear it. We hold it for the lifetime of the active
/// role and release it when the user leaves Host/Client mode.
/// </para>
/// </summary>
public sealed class SleepSuppressionService : IDisposable
{
    [Flags]
    private enum ExecutionState : uint
    {
        AwaymodeRequired = 0x00000040,
        Continuous = 0x80000000,
        DisplayRequired = 0x00000002,
        SystemRequired = 0x00000001,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private bool _active;

    public void Begin()
    {
        if (_active) return;
        // SystemRequired keeps the machine awake; DisplayRequired keeps the
        // screen on too (host needs to show the session code; client needs
        // to show "connected" status to the room operator).
        var prev = SetThreadExecutionState(
            ExecutionState.Continuous |
            ExecutionState.SystemRequired |
            ExecutionState.DisplayRequired);
        _active = prev != 0;
    }

    public void End()
    {
        if (!_active) return;
        SetThreadExecutionState(ExecutionState.Continuous);
        _active = false;
    }

    public void Dispose() => End();
}
