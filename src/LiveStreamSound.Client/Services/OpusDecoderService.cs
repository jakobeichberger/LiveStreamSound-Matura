using Concentus;
using LiveStreamSound.Shared.Audio;

namespace LiveStreamSound.Client.Services;

public sealed class OpusDecoderService
{
    private readonly IOpusDecoder _decoder;
    private readonly short[] _pcmScratch = new short[AudioFormat.SamplesPerFrame * AudioFormat.Channels];

    public OpusDecoderService()
    {
        _decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, AudioFormat.Channels);
    }

    /// <summary>
    /// Decodes an Opus payload. Returns the number of PCM bytes (16-bit little-endian) written.
    /// When <paramref name="payload"/> is empty, decodes silence / packet loss concealment.
    /// </summary>
    public int Decode(ReadOnlySpan<byte> payload, Span<byte> pcm16Output)
    {
        var samples = _decoder.Decode(
            payload,
            _pcmScratch.AsSpan(),
            AudioFormat.SamplesPerFrame);

        var interleavedSamples = samples * AudioFormat.Channels;
        var byteLen = interleavedSamples * 2;
        if (pcm16Output.Length < byteLen) return 0;

        for (var i = 0; i < interleavedSamples; i++)
        {
            var s = _pcmScratch[i];
            pcm16Output[i * 2] = (byte)(s & 0xFF);
            pcm16Output[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return byteLen;
    }
}
