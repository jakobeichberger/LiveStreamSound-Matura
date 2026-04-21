using LiveStreamSound.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LiveStreamSound.Client.Services;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Plays incoming PCM16 stereo frames on a WASAPI output device.
/// Device selection is runtime-switchable (e.g. switching to HDMI output).
/// Volume + mute are applied client-side before write.
/// </summary>
public sealed class AudioPlaybackService : IDisposable
{
    private readonly LogService _log;
    private readonly object _lock = new();
    private WasapiOut? _output;
    private BufferedWaveProvider? _provider;
    private MMDevice? _currentDevice;

    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public string? CurrentDeviceId => _currentDevice?.ID;
    public int BufferedMs => _provider?.BufferedDuration.Milliseconds ?? 0;

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

            using var enumerator = new MMDeviceEnumerator();
            _currentDevice = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(deviceId);

            var waveFormat = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels);
            _provider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(AudioFormat.MaxJitterBufferMs * 2),
                DiscardOnBufferOverflow = true,
            };

            _output = new WasapiOut(_currentDevice, AudioClientShareMode.Shared, useEventSync: true, 50);
            _output.Init(_provider);
            _output.Play();
            _log.Info("Playback", $"Started on '{_currentDevice.FriendlyName}' ({_currentDevice.ID})");
        }
    }

    public void SwitchDevice(string deviceId) => Start(deviceId);

    public void WritePcm(byte[] pcm)
    {
        var prov = _provider;
        if (prov is null) return;
        try
        {
            if (IsMuted)
            {
                prov.AddSamples(new byte[pcm.Length], 0, pcm.Length);
                return;
            }
            if (Volume < 0.999f)
            {
                var scaled = ApplyVolume(pcm, Volume);
                prov.AddSamples(scaled, 0, scaled.Length);
            }
            else
            {
                prov.AddSamples(pcm, 0, pcm.Length);
            }
        }
        catch (ObjectDisposedException) { /* Stop raced with the drain timer — drop this frame */ }
        catch (InvalidOperationException) { /* buffer full or playback state changed — drop */ }
    }

    private static byte[] ApplyVolume(byte[] pcm, float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        var result = new byte[pcm.Length];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var s = (short)(pcm[i] | (pcm[i + 1] << 8));
            var scaled = (short)(s * clamped);
            result[i] = (byte)(scaled & 0xFF);
            result[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
        return result;
    }

    public void Stop()
    {
        lock (_lock) Stop_NoLock();
    }

    private void Stop_NoLock()
    {
        try { _output?.Stop(); _output?.Dispose(); } catch { }
        _output = null;
        _provider = null;
        _currentDevice?.Dispose();
        _currentDevice = null;
    }

    public void Dispose() => Stop();
}
