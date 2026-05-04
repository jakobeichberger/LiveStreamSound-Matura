using System.Buffers;
using LiveStreamSound.Shared.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Captures system audio via WASAPI loopback and emits fixed-size 20ms PCM16 stereo frames.
/// The captured format is resampled/converted to 48 kHz / 16-bit / stereo regardless of the device format.
/// <para>
/// Allocation discipline: at 50 fps every per-frame allocation costs Gen0 GC pressure that
/// surfaces as audio glitches when a collection runs. We use a small ring of reusable
/// frame buffers (round-robin across N) so the consumer can publish FrameAvailable
/// without blocking the next capture tick.
/// </para>
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private IWaveProvider? _convertedProvider;
    private readonly object _lock = new();
    private bool _isRunning;

    // 4 buffers × 20ms = 80ms of in-flight audio. By the time we round-robin
    // back to buffer 0, its previous consumer (encoder + UDP fan-out) is
    // long done. Eliminates the per-frame `new byte[3840]` allocation.
    private const int FrameBufferRingSize = 4;
    private readonly byte[][] _frameRing = new byte[FrameBufferRingSize][];
    private int _frameRingIndex;

    public event Action<byte[]>? FrameAvailable;
    public event Action<Exception>? CaptureError;

    public bool IsRunning => _isRunning;

    public AudioCaptureService()
    {
        for (var i = 0; i < FrameBufferRingSize; i++)
            _frameRing[i] = new byte[AudioFormat.BytesPerPcmFrame];
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;

            // Dispose the previous capture before creating a new one —
            // otherwise a Stop/Start cycle (role switch, session restart)
            // leaks a WasapiLoopbackCapture COM handle each time.
            UnsubscribeAndDisposeCapture();

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
        // Snapshot field references to defend against a concurrent Dispose.
        // NAudio fires DataAvailable on its own worker thread; if Dispose
        // runs in parallel and nulls these fields we mustn't crash.
        var buffer = _buffer;
        var converted = _convertedProvider;
        if (!_isRunning || buffer is null || converted is null) return;

        try
        {
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            int bytesAvailable;
            do
            {
                var buffered = buffer.BufferedBytes;
                if (buffered == 0) break;

                // Round-robin through pre-allocated frame buffers — no per-frame
                // heap allocation in the steady state.
                var frameBuffer = _frameRing[_frameRingIndex];
                _frameRingIndex = (_frameRingIndex + 1) % FrameBufferRingSize;

                var read = converted.Read(frameBuffer, 0, frameBuffer.Length);
                if (read < frameBuffer.Length)
                {
                    Array.Clear(frameBuffer, read, frameBuffer.Length - read);
                }
                if (read > 0)
                {
                    FrameAvailable?.Invoke(frameBuffer);
                }

                bytesAvailable = buffer.BufferedBytes;
            } while (bytesAvailable >= AudioFormat.BytesPerPcmFrame / 2);
        }
        catch (Exception ex)
        {
            // Marshal the error onto the thread pool so a CaptureError
            // handler that re-enters Start() doesn't deadlock against _lock.
            _ = Task.Run(() =>
            {
                try { CaptureError?.Invoke(ex); } catch { }
            });
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _ = Task.Run(() =>
            {
                try { CaptureError?.Invoke(e.Exception); } catch { }
            });
        }
    }

    /// <summary>
    /// Detaches event handlers FIRST, then disposes the capture instance.
    /// Order matters: a DataAvailable callback that fires after Dispose but
    /// before unsubscribe will see null fields and skip safely.
    /// </summary>
    private void UnsubscribeAndDisposeCapture()
    {
        if (_capture is null) return;
        try
        {
            _capture.DataAvailable -= OnCaptureData;
            _capture.RecordingStopped -= OnRecordingStopped;
        }
        catch { }
        try { _capture.Dispose(); } catch { }
        _capture = null;
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            UnsubscribeAndDisposeCapture();
            _buffer = null;
            _convertedProvider = null;
        }
    }
}
