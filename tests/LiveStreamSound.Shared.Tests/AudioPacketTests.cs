using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

public class AudioPacketTests
{
    [Fact]
    public void WriteThenRead_RoundTripsHeaderAndPayload()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var header = new AudioPacketHeader(
            SequenceNumber: 42,
            ServerTimestampMs: 1_700_000_000_000L,
            PayloadType: AudioPayloadType.Opus,
            PayloadLength: (ushort)payload.Length);

        var buf = new byte[AudioPacket.HeaderSize + payload.Length];
        var written = AudioPacket.Write(buf, header, payload);
        Assert.Equal(buf.Length, written);

        var ok = AudioPacket.TryRead(buf, out var readHeader, out var readPayload);
        Assert.True(ok);
        Assert.Equal(header.SequenceNumber, readHeader.SequenceNumber);
        Assert.Equal(header.ServerTimestampMs, readHeader.ServerTimestampMs);
        Assert.Equal(header.PayloadType, readHeader.PayloadType);
        Assert.Equal(header.PayloadLength, readHeader.PayloadLength);
        Assert.Equal(payload, readPayload.ToArray());
    }

    [Fact]
    public void TryRead_RejectsBadMagic()
    {
        var buf = new byte[AudioPacket.HeaderSize + 4];
        buf[0] = (byte)'X'; buf[1] = (byte)'Y'; buf[2] = (byte)'Z'; buf[3] = (byte)'Q';
        Assert.False(AudioPacket.TryRead(buf, out _, out _));
    }

    [Fact]
    public void TryRead_RejectsShortBuffer()
    {
        var buf = new byte[5];
        Assert.False(AudioPacket.TryRead(buf, out _, out _));
    }
}
