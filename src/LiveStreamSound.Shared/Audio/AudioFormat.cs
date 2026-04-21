namespace LiveStreamSound.Shared.Audio;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;

    public const int FrameDurationMs = 20;
    public const int SamplesPerFrame = SampleRate * FrameDurationMs / 1000;
    public const int BytesPerPcmFrame = SamplesPerFrame * Channels * (BitsPerSample / 8);

    public const int OpusBitrate = 128_000;

    public const int JitterBufferMs = 100;
    public const int MaxJitterBufferMs = 500;
}
