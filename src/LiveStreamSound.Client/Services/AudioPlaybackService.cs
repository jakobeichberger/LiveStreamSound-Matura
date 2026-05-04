using LiveStreamSound.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace LiveStreamSound.Client.Services;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Plays incoming PCM16 stereo frames on a WASAPI output device.
/// Device selection is runtime-switchable (e.g. switching to HDMI output).
/// Volume + mute are applied client-side before write.
/// <para>
/// Subscribes to <see cref="MMNotificationClient"/> events so that when the
/// user yanks the HDMI cable mid-exam (or the default device changes for any
/// reason), playback automatically falls back to the new default endpoint
/// instead of going silently dead.
/// </para>
/// </summary>
public sealed class AudioPlaybackService : IDisposable
{
    private readonly LogService _log;
    private readonly object _lock = new();
    private WasapiOut? _output;
    private BufferedWaveProvider? _provider;
    private MMDevice? _currentDevice;
    private MMDeviceEnumerator? _deviceEnumerator;
    private DeviceChangeNotifier? _deviceNotifier;
    // In-place volume scratch — sized once, reused.
    private byte[]? _volumeScratch;

    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public string? CurrentDeviceId => _currentDevice?.ID;
    public int BufferedMs => _provider?.BufferedDuration.Milliseconds ?? 0;

    /// <summary>Raised when the playback device disappears (HDMI unplug, USB-DAC removed)
    /// and the service has fallen back to the new default endpoint. UI surfaces an info card.</summary>
    public event Action<string>? DeviceFellBack;

    public AudioPlaybackService(LogService log) { _log = log; }

    public static IReadOnlyList<AudioOutputDevice> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        string? defaultId = null;
        using (var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            defaultId = defaultDevice.ID;

        var list = new List<AudioOutputDevice>();
        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                list.Add(new AudioOutputDevice(dev.ID, dev.FriendlyName, dev.ID == defaultId));
            }
            finally
            {
                dev.Dispose();
            }
        }
        return list;
    }

    public void Start(string? deviceId = null)
    {
        lock (_lock)
        {
            Stop_NoLock();

            _deviceEnumerator = new MMDeviceEnumerator();
            _currentDevice = deviceId is null
                ? _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : _deviceEnumerator.GetDevice(deviceId);

            var waveFormat = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels);
            _provider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(AudioFormat.MaxJitterBufferMs * 2),
                DiscardOnBufferOverflow = true,
            };

            _output = new WasapiOut(_currentDevice, AudioClientShareMode.Shared, useEventSync: true, 50);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_provider);
            _output.Play();

            // Subscribe to system-wide audio device changes so we can react to
            // an HDMI unplug or default-device-changed event.
            _deviceNotifier = new DeviceChangeNotifier(this);
            try { _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceNotifier); }
            catch (Exception ex) { _log.Warn("Playback", "Could not register device change notifier", ex); }

            _log.Info("Playback", $"Started on '{_currentDevice.FriendlyName}' ({_currentDevice.ID})");
        }
    }

    public void SwitchDevice(string deviceId) => Start(deviceId);

    /// <summary>
    /// Write a PCM frame. Volume is applied in-place into a reusable scratch
    /// buffer so we don't churn Gen0 at 50fps. Mute drops the frame entirely
    /// — feeding silence into BufferedWaveProvider would just bloat the buffer.
    /// </summary>
    public void WritePcm(byte[] pcm) => WritePcm(pcm, pcm.Length);

    public void WritePcm(byte[] pcm, int length)
    {
        var prov = _provider;
        if (prov is null) return;
        if (IsMuted) return; // don't enqueue silence — it just steals buffer space
        try
        {
            if (Volume < 0.999f)
            {
                if (_volumeScratch is null || _volumeScratch.Length < length)
                    _volumeScratch = new byte[length];
                ApplyVolumeInto(pcm, length, _volumeScratch, Volume);
                prov.AddSamples(_volumeScratch, 0, length);
            }
            else
            {
                prov.AddSamples(pcm, 0, length);
            }
        }
        catch (ObjectDisposedException) { /* Stop raced with the drain timer — drop this frame */ }
        catch (InvalidOperationException) { /* buffer full or playback state changed — drop */ }
    }

    private static void ApplyVolumeInto(byte[] pcm, int length, byte[] dest, float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        for (var i = 0; i < length; i += 2)
        {
            var s = (short)(pcm[i] | (pcm[i + 1] << 8));
            var scaled = (short)(s * clamped);
            dest[i] = (byte)(scaled & 0xFF);
            dest[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        // Most common cause: device invalidated (HDMI unplugged, USB DAC removed).
        // Fall back to the system default endpoint so the audio resumes
        // without manual intervention.
        _log.Warn("Playback",
            $"Playback stopped with exception, falling back to default device", e.Exception);
        try
        {
            Start(deviceId: null);
            DeviceFellBack?.Invoke(_currentDevice?.FriendlyName ?? "default");
        }
        catch (Exception ex)
        {
            _log.Error("Playback", "Could not fall back to default device", ex);
        }
    }

    public void Stop()
    {
        lock (_lock) Stop_NoLock();
    }

    private void Stop_NoLock()
    {
        if (_deviceNotifier is not null && _deviceEnumerator is not null)
        {
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceNotifier); } catch { }
            _deviceNotifier = null;
        }
        try
        {
            if (_output is not null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
            }
        }
        catch { }
        _output = null;
        _provider = null;
        _currentDevice?.Dispose();
        _currentDevice = null;
        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// IMMNotificationClient bridge. NAudio requires us to implement this
    /// interface ourselves; we forward only the events we care about.
    /// </summary>
    private sealed class DeviceChangeNotifier : IMMNotificationClient
    {
        private readonly AudioPlaybackService _owner;
        public DeviceChangeNotifier(AudioPlaybackService owner) { _owner = owner; }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            // Currently-used device went unplugged → fall back.
            if (newState != DeviceState.Active &&
                string.Equals(deviceId, _owner._currentDevice?.ID, StringComparison.OrdinalIgnoreCase))
            {
                _owner._log.Warn("Playback",
                    $"Active device {deviceId} state changed to {newState}, falling back");
                _ = Task.Run(() =>
                {
                    try { _owner.Start(deviceId: null); } catch { }
                    _owner.DeviceFellBack?.Invoke(_owner._currentDevice?.FriendlyName ?? "default");
                });
            }
        }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId)
        {
            if (string.Equals(deviceId, _owner._currentDevice?.ID, StringComparison.OrdinalIgnoreCase))
            {
                _owner._log.Warn("Playback", $"Active device {deviceId} removed, falling back to default");
                _ = Task.Run(() =>
                {
                    try { _owner.Start(deviceId: null); } catch { }
                    _owner.DeviceFellBack?.Invoke(_owner._currentDevice?.FriendlyName ?? "default");
                });
            }
        }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
