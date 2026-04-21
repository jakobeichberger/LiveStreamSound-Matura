using System.Buffers.Binary;

namespace LiveStreamSound.Shared.Protocol;

public enum AudioPayloadType : byte
{
    Pcm16 = 0,
    Opus = 1,
}

public readonly record struct AudioPacketHeader(
    uint SequenceNumber,
    long ServerTimestampMs,
    AudioPayloadType PayloadType,
    ushort PayloadLength);

public static class AudioPacket
{
    public const int HeaderSize = 20;
    public static readonly byte[] Magic = "LSSA"u8.ToArray();
    public const byte Version = 1;

    public const int MaxPacketSize = 1400;

    public static int Write(
        Span<byte> buffer,
        AudioPacketHeader header,
        ReadOnlySpan<byte> payload)
    {
        if (buffer.Length < HeaderSize + payload.Length)
            throw new ArgumentException("Buffer too small", nameof(buffer));
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentException("Payload too large", nameof(payload));

        Magic.CopyTo(buffer);
        buffer[4] = Version;
        buffer[5] = (byte)header.PayloadType;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), header.SequenceNumber);
        BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(12, 8), header.ServerTimestampMs);
        payload.CopyTo(buffer.Slice(HeaderSize));

        return HeaderSize + payload.Length;
    }

    public static bool TryRead(
        ReadOnlySpan<byte> buffer,
        out AudioPacketHeader header,
        out ReadOnlySpan<byte> payload)
    {
        header = default;
        payload = default;

        if (buffer.Length < HeaderSize) return false;
        if (!buffer[..4].SequenceEqual(Magic)) return false;
        if (buffer[4] != Version) return false;

        var payloadType = (AudioPayloadType)buffer[5];
        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(6, 2));
        var seq = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8, 4));
        var ts = BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(12, 8));

        if (buffer.Length < HeaderSize + payloadLength) return false;

        header = new AudioPacketHeader(seq, ts, payloadType, payloadLength);
        payload = buffer.Slice(HeaderSize, payloadLength);
        return true;
    }
}
