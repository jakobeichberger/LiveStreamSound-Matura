using LiveStreamSound.Shared.Audio;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Generates a 440 Hz sine-tone (a-440 reference) as 20 ms PCM16 stereo frames
/// and feeds them through the same FrameAvailable pipeline as the WASAPI
/// loopback capture. The teacher uses this for pre-flight verification:
/// "everyone shows the green checkmark when the test tone plays".
/// <para>
/// Default duration 10 seconds. The tone fades in + out over 200ms each side
/// so it doesn't sound like a click.
/// </para>
/// </summary>
public sealed class TestToneService : IDisposable
{
    private readonly LogService _log;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<byte[]>? FrameAvailable;
    public bool IsRunning => _task is { IsCompleted: false };

    public TestToneService(LogService log) { _log = log; }

    /// <summary>
    /// Start emitting a test tone. <paramref name="durationMs"/> default 10 s,
    /// <paramref name="frequencyHz"/> default 440 Hz, <paramref name="amplitude"/> 0..1.
    /// </summary>
    public void Start(int durationMs = 10_000, int frequencyHz = 440, float amplitude = 0.3f)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(durationMs, frequencyHz, amplitude, _cts.Token));
        _log.Info("TestTone", $"Test tone started: {frequencyHz}Hz, {durationMs}ms, amp={amplitude:F2}");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _task?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task RunAsync(int durationMs, int frequencyHz, float amplitude, CancellationToken ct)
    {
        const int frameMs = 20;
        const int sampleRate = AudioFormat.SampleRate;
        const int channels = AudioFormat.Channels;
        const int samplesPerFrame = sampleRate * frameMs / 1000;
        const int bytesPerFrame = samplesPerFrame * channels * sizeof(short);

        var totalFrames = durationMs / frameMs;
        var fadeFrames = 200 / frameMs; // 10 frames fade in + 10 fade out

        var phase = 0.0;
        var phaseInc = 2 * Math.PI * frequencyHz / sampleRate;

        var nextFrameAt = DateTimeOffset.UtcNow;
        for (var frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            if (ct.IsCancellationRequested) return;

            // Apply linear fade-in/out so we don't click into a 50fps sine.
            float fade = 1f;
            if (frameIdx < fadeFrames)
                fade = (float)frameIdx / fadeFrames;
            else if (frameIdx >= totalFrames - fadeFrames)
                fade = (float)(totalFrames - 1 - frameIdx) / fadeFrames;

            var buffer = new byte[bytesPerFrame];
            for (var s = 0; s < samplesPerFrame; s++)
            {
                var sample = (short)(Math.Sin(phase) * amplitude * fade * short.MaxValue);
                phase += phaseInc;
                if (phase > 2 * Math.PI) phase -= 2 * Math.PI;

                // Stereo: same sample to both channels.
                for (var ch = 0; ch < channels; ch++)
                {
                    var idx = (s * channels + ch) * sizeof(short);
                    buffer[idx] = (byte)(sample & 0xFF);
                    buffer[idx + 1] = (byte)((sample >> 8) & 0xFF);
                }
            }

            FrameAvailable?.Invoke(buffer);

            nextFrameAt = nextFrameAt.AddMilliseconds(frameMs);
            var wait = nextFrameAt - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        _log.Info("TestTone", "Test tone finished");
    }

    public void Dispose() => Stop();
}
