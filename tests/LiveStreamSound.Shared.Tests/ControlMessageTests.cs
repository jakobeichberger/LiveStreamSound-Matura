using System.IO;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// Full coverage for the JSON control protocol: round-tripping of every
/// defined message, framing layer (length-prefix + UTF-8), rejection of
/// invalid frames, concurrent message stream decoding.
/// </summary>
public class ControlMessageTests
{
    [Fact]
    public void Hello_RoundTripsThroughJson()
    {
        ControlMessage original = new Hello("428193", "Raum 17", 1);
        var json = MessageJson.Serialize(original);

        Assert.Contains("\"type\":\"hello\"", json);
        Assert.Contains("428193", json);

        var roundTripped = MessageJson.Deserialize(json);
        Assert.IsType<Hello>(roundTripped);
        var h = (Hello)roundTripped!;
        Assert.Equal("428193", h.Code);
        Assert.Equal("Raum 17", h.ClientName);
        Assert.Equal(1, h.ProtocolVersion);
    }

    [Fact]
    public void Welcome_RoundTrip_PreservesAllFields()
    {
        var w = new Welcome(
            ClientId: "abc123def456",
            AudioUdpPort: 5001,
            SampleRate: 48000,
            Channels: 2,
            AudioCodec: "opus",
            ServerTimeMs: 1_700_000_000_000L);
        var json = MessageJson.Serialize(w);
        var rt = (Welcome)MessageJson.Deserialize(json)!;
        Assert.Equal(w, rt);
    }

    [Fact]
    public void AuthFail_RoundTrip()
    {
        var a = new AuthFail("RATE_LIMITED");
        var rt = (AuthFail)MessageJson.Deserialize(MessageJson.Serialize(a))!;
        Assert.Equal("RATE_LIMITED", rt.Reason);
    }

    [Fact]
    public void Invitation_RoundTripsThroughJson()
    {
        ControlMessage original = new Invitation("100200", "192.168.1.42", 5000, "Lehrer-Laptop");
        var json = MessageJson.Serialize(original);

        Assert.Contains("\"type\":\"invitation\"", json);

        var rt = MessageJson.Deserialize(json);
        Assert.IsType<Invitation>(rt);
        var inv = (Invitation)rt!;
        Assert.Equal("100200", inv.SessionCode);
        Assert.Equal("192.168.1.42", inv.HostAddress);
        Assert.Equal(5000, inv.HostControlPort);
        Assert.Equal("Lehrer-Laptop", inv.HostDisplayName);
    }

    [Fact]
    public void SetVolume_RoundTripsThroughJson()
    {
        ControlMessage original = new SetVolume(0.75f);
        var json = MessageJson.Serialize(original);
        var rt = MessageJson.Deserialize(json);
        Assert.IsType<SetVolume>(rt);
        Assert.Equal(0.75f, ((SetVolume)rt!).Level);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(0.42f)]
    [InlineData(0.001f)]
    public void SetVolume_EdgeValues_RoundTrip(float level)
    {
        var rt = (SetVolume)MessageJson.Deserialize(MessageJson.Serialize(new SetVolume(level)))!;
        Assert.Equal(level, rt.Level);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetMute_BothStates(bool muted)
    {
        var rt = (SetMute)MessageJson.Deserialize(MessageJson.Serialize(new SetMute(muted)))!;
        Assert.Equal(muted, rt.Muted);
    }

    [Fact]
    public void ListOutputDevicesRequest_HasNoBody()
    {
        var json = MessageJson.Serialize(new ListOutputDevicesRequest());
        var rt = MessageJson.Deserialize(json);
        Assert.IsType<ListOutputDevicesRequest>(rt);
    }

    [Fact]
    public void OutputDevicesResponse_RoundTrip()
    {
        var resp = new OutputDevicesResponse(
            new[]
            {
                new OutputDeviceInfo("dev-1", "Laptop Speakers", true),
                new OutputDeviceInfo("dev-2", "HDMI 1", false),
                new OutputDeviceInfo("dev-3", "Bluetooth-Kopfhörer", false),
            },
            CurrentDeviceId: "dev-2");
        var rt = (OutputDevicesResponse)MessageJson.Deserialize(MessageJson.Serialize(resp))!;
        Assert.Equal(3, rt.Devices.Length);
        Assert.Equal("HDMI 1", rt.Devices[1].Name);
        Assert.True(rt.Devices[0].IsDefault);
        Assert.Equal("dev-2", rt.CurrentDeviceId);
    }

    [Fact]
    public void SetOutputDevice_RoundTrip()
    {
        var rt = (SetOutputDevice)MessageJson.Deserialize(
            MessageJson.Serialize(new SetOutputDevice("hdmi-out-42")))!;
        Assert.Equal("hdmi-out-42", rt.DeviceId);
    }

    [Fact]
    public void Kick_RoundTrip()
    {
        var rt = (Kick)MessageJson.Deserialize(MessageJson.Serialize(new Kick("host ended session")))!;
        Assert.Equal("host ended session", rt.Reason);
    }

    [Fact]
    public void Ping_Pong_ClockValues_RoundTrip()
    {
        var p = new Ping(1_700_000_000_123L);
        var po = new Pong(1_700_000_000_123L, 1_700_000_000_456L);

        var rtP = (Ping)MessageJson.Deserialize(MessageJson.Serialize(p))!;
        Assert.Equal(p.ClientTimeMs, rtP.ClientTimeMs);

        var rtPo = (Pong)MessageJson.Deserialize(MessageJson.Serialize(po))!;
        Assert.Equal(po.ClientTimeMs, rtPo.ClientTimeMs);
        Assert.Equal(po.ServerTimeMs, rtPo.ServerTimeMs);
    }

    [Fact]
    public void ClientStatus_RoundTrip_WithAllFields()
    {
        var s = new ClientStatus(0.6f, true, "hdmi-x", 135);
        var rt = (ClientStatus)MessageJson.Deserialize(MessageJson.Serialize(s))!;
        Assert.Equal(s, rt);
    }

    [Fact]
    public void SessionEnding_RoundTrip()
    {
        var rt = (SessionEnding)MessageJson.Deserialize(
            MessageJson.Serialize(new SessionEnding("teacher stopped")))!;
        Assert.Equal("teacher stopped", rt.Reason);
    }

    [Fact]
    public void SerializeFrame_LengthPrefix_Matches()
    {
        var frame = MessageJson.SerializeFrame(new Hello("000001", "x", 1));
        // First 4 bytes = big-endian length
        var length = (frame[0] << 24) | (frame[1] << 16) | (frame[2] << 8) | frame[3];
        Assert.Equal(frame.Length - 4, length);
    }

    [Fact]
    public async Task WriteFrameAsync_Then_ReadFrameAsync_RoundTripsOverStream()
    {
        using var ms = new MemoryStream();
        var original = new Hello("CODE42", "TestClient", 1);
        await MessageJson.WriteFrameAsync(ms, original);
        ms.Position = 0;
        var rt = await MessageJson.ReadFrameAsync(ms);
        Assert.IsType<Hello>(rt);
        Assert.Equal(original, (Hello)rt!);
    }

    [Fact]
    public async Task ReadFrameAsync_MultipleMessagesInOneStream_AllParseIndependently()
    {
        using var ms = new MemoryStream();
        ControlMessage[] all =
        {
            new Hello("000001", "A", 1),
            new Welcome("id-1", 5001, 48000, 2, "opus", 0),
            new Ping(1),
            new SetVolume(0.3f),
            new Kick("done"),
        };
        foreach (var m in all) await MessageJson.WriteFrameAsync(ms, m);

        ms.Position = 0;
        for (var i = 0; i < all.Length; i++)
        {
            var rt = await MessageJson.ReadFrameAsync(ms);
            Assert.NotNull(rt);
            Assert.Equal(all[i].GetType(), rt!.GetType());
        }

        // Exhausted → null
        var eof = await MessageJson.ReadFrameAsync(ms);
        Assert.Null(eof);
    }

    [Fact]
    public async Task ReadFrameAsync_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        Assert.Null(await MessageJson.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task ReadFrameAsync_NegativeLength_Throws()
    {
        using var ms = new MemoryStream();
        // Forge a frame with a negative length prefix.
        ms.WriteByte(0xFF); ms.WriteByte(0xFF); ms.WriteByte(0xFF); ms.WriteByte(0xFF);
        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MessageJson.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task ReadFrameAsync_RidiculouslyLargeLength_Throws()
    {
        using var ms = new MemoryStream();
        // 2 MB prefix exceeds the 1 MiB cap.
        var big = 2 * 1024 * 1024;
        ms.WriteByte((byte)(big >> 24));
        ms.WriteByte((byte)(big >> 16));
        ms.WriteByte((byte)(big >> 8));
        ms.WriteByte((byte)big);
        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MessageJson.ReadFrameAsync(ms));
    }

    [Fact]
    public void AudioClientReady_RoundTripsThroughJson()
    {
        ControlMessage original = new AudioClientReady(53914);
        var json = MessageJson.Serialize(original);

        Assert.Contains("\"type\":\"audioClientReady\"", json);

        var rt = MessageJson.Deserialize(json);
        Assert.IsType<AudioClientReady>(rt);
        Assert.Equal(53914, ((AudioClientReady)rt!).ClientUdpPort);
    }

    [Fact]
    public void InvitationResponse_RoundTripsThroughJson()
    {
        ControlMessage original = new InvitationResponse(true, null);
        var json = MessageJson.Serialize(original);
        var rt = MessageJson.Deserialize(json);
        Assert.IsType<InvitationResponse>(rt);
        var ir = (InvitationResponse)rt!;
        Assert.True(ir.Accepted);
        Assert.Null(ir.Reason);
    }

    [Fact]
    public void InvitationResponse_WithReason_RoundTrips()
    {
        ControlMessage original = new InvitationResponse(false, "declined");
        var json = MessageJson.Serialize(original);
        var rt = MessageJson.Deserialize(json);
        var ir = (InvitationResponse)rt!;
        Assert.False(ir.Accepted);
        Assert.Equal("declined", ir.Reason);
    }

    [Fact]
    public void Deserialize_UnknownType_ReturnsNullOrThrows()
    {
        // The polymorphic JSON deserializer throws for unknown type discriminators.
        const string unknownJson = "{\"type\":\"unknown-future-message\"}";
        var ex = Record.Exception(() => MessageJson.Deserialize(unknownJson));
        Assert.NotNull(ex);
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws()
    {
        Assert.ThrowsAny<Exception>(() => MessageJson.Deserialize("not-even-json"));
        Assert.ThrowsAny<Exception>(() => MessageJson.Deserialize("{"));
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var json = MessageJson.Serialize(new Welcome("id", 5001, 48000, 2, "opus", 99));
        // camelCase from JsonSerializerDefaults.Web
        Assert.Contains("\"clientId\":\"id\"", json);
        Assert.Contains("\"audioUdpPort\":5001", json);
        Assert.Contains("\"sampleRate\":48000", json);
        Assert.Contains("\"serverTimeMs\":99", json);
    }

    [Fact]
    public void Serialize_IncludesTypeDiscriminatorFirst()
    {
        // The type discriminator *must* be the first property so the streaming
        // deserializer can pick the right concrete type.
        var json = MessageJson.Serialize(new Hello("x", "y", 1));
        Assert.StartsWith("{\"type\":", json);
    }

    [Fact]
    public async Task ReadFrameAsync_PartialLengthPrefix_ReturnsNull()
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.Position = 0;
        // Only 2 bytes — the 4-byte length prefix can't be completed.
        Assert.Null(await MessageJson.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task ReadFrameAsync_PartialPayloadAfterHeader_ReturnsNull()
    {
        using var ms = new MemoryStream();
        // Valid length prefix for a 20-byte payload but we only write 5 bytes.
        ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(20);
        ms.Write(new byte[] { 1, 2, 3, 4, 5 });
        ms.Position = 0;
        Assert.Null(await MessageJson.ReadFrameAsync(ms));
    }
}
