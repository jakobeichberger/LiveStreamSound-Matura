using LiveStreamSound.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Captures system audio via WASAPI loopback and emits fixed-size 20ms PCM16 stereo frames.
/// The captured format is resampled/converted to 48 kHz / 16-bit / stereo regardless of the device format.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private IWaveProvider? _convertedProvider;
    private readonly object _lock = new();
    private bool _isRunning;

    public event Action<byte[]>? FrameAvailable;
    public event Action<Exception>? CaptureError;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;

            _capture = new WasapiLoopbackCapture();
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2),
            };

            var target = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels);
            ISampleProvider source = _buffer.ToSampleProvider();
            if (source.WaveFormat.Channels != target.Channels)
            {
                source = source.WaveFormat.Channels == 1
                    ? new MonoToStereoSampleProvider(source)
                    : new StereoToMonoSampleProvider(source);
            }
            if (source.WaveFormat.SampleRate != target.SampleRate)
            {
                source = new WdlResamplingSampleProvider(source, target.SampleRate);
            }

            _convertedProvider = source.ToWaveProvider16();

            _capture.DataAvailable += OnCaptureData;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            try { _capture?.StopRecording(); } catch { /* best effort */ }
        }
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (_buffer is null || _convertedProvider is null) return;

        try
        {
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            Span<byte> frame = stackalloc byte[AudioFormat.BytesPerPcmFrame];
            int bytesAvailable;
            do
            {
                var buffered = _buffer.BufferedBytes;
                if (buffered == 0) break;

                // Read enough from converted provider to produce one target frame
                var frameBuffer = new byte[AudioFormat.BytesPerPcmFrame];
                var read = _convertedProvider.Read(frameBuffer, 0, frameBuffer.Length);
                if (read < frameBuffer.Length)
                {
                    Array.Clear(frameBuffer, read, frameBuffer.Length - read);
                }
                if (read > 0)
                {
                    FrameAvailable?.Invoke(frameBuffer);
                }

                bytesAvailable = _buffer.BufferedBytes;
            } while (bytesAvailable >= AudioFormat.BytesPerPcmFrame / 2);
        }
        catch (Exception ex)
        {
            CaptureError?.Invoke(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            CaptureError?.Invoke(e.Exception);
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            _capture?.Dispose();
            _capture = null;
            _buffer = null;
            _convertedProvider = null;
        }
    }
}
