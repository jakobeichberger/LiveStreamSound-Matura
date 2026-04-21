using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

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

    [Fact]
    public void SerializeFrame_LengthPrefix_Matches()
    {
        var frame = MessageJson.SerializeFrame(new Hello("000001", "x", 1));
        // First 4 bytes = big-endian length
        var length = (frame[0] << 24) | (frame[1] << 16) | (frame[2] << 8) | frame[3];
        Assert.Equal(frame.Length - 4, length);
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
}
