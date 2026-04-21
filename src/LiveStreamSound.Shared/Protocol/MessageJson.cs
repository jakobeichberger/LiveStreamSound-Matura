using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace LiveStreamSound.Shared.Protocol;

public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string Serialize(ControlMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static ControlMessage? Deserialize(string json) =>
        JsonSerializer.Deserialize<ControlMessage>(json, Options);

    public static byte[] SerializeFrame(ControlMessage message)
    {
        var payload = Encoding.UTF8.GetBytes(Serialize(message));
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    public static async Task WriteFrameAsync(
        Stream stream,
        ControlMessage message,
        CancellationToken cancellationToken = default)
    {
        var frame = SerializeFrame(message);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ControlMessage?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        if (!await TryReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > 1_048_576)
            throw new InvalidDataException($"Invalid control frame length: {length}");

        var payload = new byte[length];
        if (!await TryReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            return null;

        var json = Encoding.UTF8.GetString(payload);
        return Deserialize(json);
    }

    private static async Task<bool> TryReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }
}
