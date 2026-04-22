using NAudio.CoreAudioApi;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Mutes the Host machine's default playback device while a session is running
/// (so the teacher's laptop speakers stay quiet while clients hear the audio
/// over their HDMI/beamer setup) and restores the previous mute state on stop.
///
/// Uses WASAPI / Core Audio API via NAudio's <see cref="MMDeviceEnumerator"/>.
/// Loopback capture (which feeds the stream) reads the audio session's mix
/// before per-endpoint volume/mute is applied, so muting the speakers does
/// NOT silence what's sent to clients.
/// </summary>
public sealed class HostMonitorMuteService : IDisposable
{
    private readonly LogService _log;
    private MMDevice? _device;
    private bool? _previousMuteState;
    private bool _muteApplied;

    public HostMonitorMuteService(LogService log) { _log = log; }

    /// <summary>
    /// Captures the default playback device's current mute state and forces it
    /// to muted. No-op + warning logged on failure (audio still streams to clients).
    /// </summary>
    public void Mute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _previousMuteState = _device.AudioEndpointVolume.Mute;
            if (_previousMuteState == true)
            {
                _log.Info("HostMonitorMute",
                    "Default playback device already muted — leaving as-is");
                return;
            }
            _device.AudioEndpointVolume.Mute = true;
            _muteApplied = true;
            _log.Info("HostMonitorMute",
                $"Muted default playback device '{_device.FriendlyName}' for session");
        }
        catch (Exception ex)
        {
            _log.Warn("HostMonitorMute",
                "Could not mute host playback (audio still streams to clients)", ex);
        }
    }

    /// <summary>Restores the device's pre-mute state. Safe to call without a prior <see cref="Mute"/>.</summary>
    public void Restore()
    {
        if (!_muteApplied || _device is null)
        {
            _device?.Dispose();
            _device = null;
            _previousMuteState = null;
            _muteApplied = false;
            return;
        }
        try
        {
            _device.AudioEndpointVolume.Mute = _previousMuteState ?? false;
            _log.Info("HostMonitorMute",
                $"Restored mute state on '{_device.FriendlyName}' to {_previousMuteState}");
        }
        catch (Exception ex)
        {
            _log.Warn("HostMonitorMute", "Could not restore host mute state", ex);
        }
        finally
        {
            _device.Dispose();
            _device = null;
            _previousMuteState = null;
            _muteApplied = false;
        }
    }

    public void Dispose()
    {
        if (_muteApplied) Restore();
        _device?.Dispose();
        _device = null;
    }
}
