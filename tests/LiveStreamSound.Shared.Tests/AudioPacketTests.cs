using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// Binary wire-format coverage for AudioPacket. These frames fire 50×/sec at peak
/// so any regression in byte-layout breaks audio silently. Tests assert every
/// offset against the documented header layout.
/// </summary>
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

    [Fact]
    public void Write_LaysBytesAtDocumentedOffsets()
    {
        var payload = new byte[] { 0xAA, 0xBB };
        var header = new AudioPacketHeader(
            SequenceNumber: 0x12345678u,
            ServerTimestampMs: 0x0102030405060708L,
            PayloadType: AudioPayloadType.Opus,
            PayloadLength: 2);
        var buf = new byte[AudioPacket.HeaderSize + payload.Length];
        AudioPacket.Write(buf, header, payload);

        // Magic "LSSA"
        Assert.Equal((byte)'L', buf[0]);
        Assert.Equal((byte)'S', buf[1]);
        Assert.Equal((byte)'S', buf[2]);
        Assert.Equal((byte)'A', buf[3]);
        // Version
        Assert.Equal(AudioPacket.Version, buf[4]);
        // Payload type
        Assert.Equal((byte)AudioPayloadType.Opus, buf[5]);
        // Payload length (big-endian ushort)
        Assert.Equal(0x00, buf[6]);
        Assert.Equal(0x02, buf[7]);
        // Sequence number (big-endian uint)
        Assert.Equal(0x12, buf[8]);
        Assert.Equal(0x34, buf[9]);
        Assert.Equal(0x56, buf[10]);
        Assert.Equal(0x78, buf[11]);
        // Timestamp (big-endian int64)
        Assert.Equal(0x01, buf[12]);
        Assert.Equal(0x02, buf[13]);
        Assert.Equal(0x03, buf[14]);
        Assert.Equal(0x04, buf[15]);
        Assert.Equal(0x05, buf[16]);
        Assert.Equal(0x06, buf[17]);
        Assert.Equal(0x07, buf[18]);
        Assert.Equal(0x08, buf[19]);
        // Payload
        Assert.Equal(0xAA, buf[20]);
        Assert.Equal(0xBB, buf[21]);
    }

    [Fact]
    public void TryRead_RejectsWrongVersion()
    {
        var buf = new byte[AudioPacket.HeaderSize];
        AudioPacket.Magic.CopyTo(buf, 0);
        buf[4] = (byte)(AudioPacket.Version + 99); // future version
        Assert.False(AudioPacket.TryRead(buf, out _, out _));
    }

    [Fact]
    public void Write_ZeroLengthPayload_Succeeds()
    {
        var header = new AudioPacketHeader(1, 1234L, AudioPayloadType.Pcm16, 0);
        var buf = new byte[AudioPacket.HeaderSize];
        var len = AudioPacket.Write(buf, header, ReadOnlySpan<byte>.Empty);
        Assert.Equal(AudioPacket.HeaderSize, len);

        Assert.True(AudioPacket.TryRead(buf, out var h, out var p));
        Assert.Equal(0, h.PayloadLength);
        Assert.Equal(0, p.Length);
    }

    [Fact]
    public void Write_ThrowsWhenBufferTooSmall()
    {
        var payload = new byte[100];
        var buf = new byte[AudioPacket.HeaderSize + 50];
        var header = new AudioPacketHeader(0, 0, AudioPayloadType.Pcm16, 100);
        var ex = Assert.Throws<ArgumentException>(() =>
            AudioPacket.Write(buf, header, payload));
        Assert.Contains("too small", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_ThrowsWhenPayloadTooLarge()
    {
        var payload = new byte[ushort.MaxValue + 1];
        var buf = new byte[AudioPacket.HeaderSize + payload.Length];
        var header = new AudioPacketHeader(0, 0, AudioPayloadType.Pcm16, 0);
        var ex = Assert.Throws<ArgumentException>(() =>
            AudioPacket.Write(buf, header, payload));
        Assert.Contains("too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_TruncatedPayload_Rejected()
    {
        var payload = new byte[50];
        var buf = new byte[AudioPacket.HeaderSize + 50];
        var header = new AudioPacketHeader(1, 1, AudioPayloadType.Opus, 50);
        AudioPacket.Write(buf, header, payload);

        // Chop off the last 10 bytes of the payload.
        var truncated = buf.AsSpan(0, AudioPacket.HeaderSize + 40);
        Assert.False(AudioPacket.TryRead(truncated, out _, out _));
    }

    [Fact]
    public void Roundtrip_MaxSequenceNumberAndTimestamp()
    {
        var header = new AudioPacketHeader(
            SequenceNumber: uint.MaxValue,
            ServerTimestampMs: long.MaxValue,
            PayloadType: AudioPayloadType.Opus,
            PayloadLength: 3);
        var payload = new byte[] { 9, 8, 7 };
        var buf = new byte[AudioPacket.HeaderSize + payload.Length];
        AudioPacket.Write(buf, header, payload);

        Assert.True(AudioPacket.TryRead(buf, out var h, out var p));
        Assert.Equal(uint.MaxValue, h.SequenceNumber);
        Assert.Equal(long.MaxValue, h.ServerTimestampMs);
        Assert.Equal(AudioPayloadType.Opus, h.PayloadType);
        Assert.Equal(payload, p.ToArray());
    }

    [Theory]
    [InlineData(AudioPayloadType.Pcm16)]
    [InlineData(AudioPayloadType.Opus)]
    public void Roundtrip_BothPayloadTypes(AudioPayloadType type)
    {
        var header = new AudioPacketHeader(123, 456, type, 4);
        var payload = new byte[] { 1, 2, 3, 4 };
        var buf = new byte[AudioPacket.HeaderSize + payload.Length];
        AudioPacket.Write(buf, header, payload);

        Assert.True(AudioPacket.TryRead(buf, out var h, out _));
        Assert.Equal(type, h.PayloadType);
    }

    [Fact]
    public void TryRead_EmptyBuffer_Rejected()
    {
        Assert.False(AudioPacket.TryRead(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void TryRead_JustMagic_Rejected()
    {
        var buf = new byte[4];
        AudioPacket.Magic.CopyTo(buf, 0);
        Assert.False(AudioPacket.TryRead(buf, out _, out _));
    }

    [Fact]
    public void HeaderSize_MatchesDocumentedLayout()
    {
        // Magic(4) + Version(1) + PayloadType(1) + PayloadLen(2) + Seq(4) + Timestamp(8) = 20 bytes.
        Assert.Equal(20, AudioPacket.HeaderSize);
    }
}
