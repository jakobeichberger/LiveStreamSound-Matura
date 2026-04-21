using Concentus;
using Concentus.Enums;
using LiveStreamSound.Shared.Audio;

namespace LiveStreamSound.Host.Services;

/// <summary>
/// Encodes 20ms PCM16 stereo frames to Opus packets. Thread-safe via single-threaded use by the capture pipeline.
/// </summary>
public sealed class OpusEncoderService
{
    private readonly IOpusEncoder _encoder;
    private readonly short[] _pcmScratch = new short[AudioFormat.SamplesPerFrame * AudioFormat.Channels];

    public OpusEncoderService()
    {
        _encoder = OpusCodecFactory.CreateEncoder(
            AudioFormat.SampleRate,
            AudioFormat.Channels,
            OpusApplication.OPUS_APPLICATION_AUDIO);
        _encoder.Bitrate = AudioFormat.OpusBitrate;
        _encoder.UseVBR = true;
        _encoder.Complexity = 8;
    }

    public int Encode(ReadOnlySpan<byte> pcm16Frame, Span<byte> output)
    {
        if (pcm16Frame.Length != AudioFormat.BytesPerPcmFrame)
            throw new ArgumentException($"Expected {AudioFormat.BytesPerPcmFrame} bytes, got {pcm16Frame.Length}", nameof(pcm16Frame));

        for (var i = 0; i < _pcmScratch.Length; i++)
        {
            _pcmScratch[i] = (short)(pcm16Frame[i * 2] | (pcm16Frame[i * 2 + 1] << 8));
        }

        return _encoder.Encode(_pcmScratch.AsSpan(), AudioFormat.SamplesPerFrame, output, output.Length);
    }
}
