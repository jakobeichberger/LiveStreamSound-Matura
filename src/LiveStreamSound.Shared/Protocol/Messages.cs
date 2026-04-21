namespace LiveStreamSound.Shared.Protocol;

public record Hello(
    string Code,
    string ClientName,
    int ProtocolVersion
) : ControlMessage;

public record Welcome(
    string ClientId,
    int AudioUdpPort,
    int SampleRate,
    int Channels,
    string AudioCodec,
    long ServerTimeMs
) : ControlMessage;

public record AuthFail(string Reason) : ControlMessage;

public record SetVolume(float Level) : ControlMessage;

public record SetMute(bool Muted) : ControlMessage;

public record ListOutputDevicesRequest : ControlMessage;

public record OutputDeviceInfo(string Id, string Name, bool IsDefault);

public record OutputDevicesResponse(
    OutputDeviceInfo[] Devices,
    string? CurrentDeviceId
) : ControlMessage;

public record SetOutputDevice(string DeviceId) : ControlMessage;

public record Kick(string Reason) : ControlMessage;

public record Ping(long ClientTimeMs) : ControlMessage;

public record Pong(long ClientTimeMs, long ServerTimeMs) : ControlMessage;

public record ClientStatus(
    float CurrentVolume,
    bool IsMuted,
    string? CurrentDeviceId,
    int BufferedMs
) : ControlMessage;

public record SessionEnding(string Reason) : ControlMessage;

/// <summary>
/// Sent from a Host to an idle Client on TCP DefaultIdleClientPort.
/// The Client shows an Accept/Reject prompt and responds with <see cref="InvitationResponse"/>.
/// On Accept the Client initiates a normal HELLO flow to the Host at HostAddress:HostControlPort,
/// so no role reversal happens at the protocol level.
/// </summary>
public record Invitation(
    string SessionCode,
    string HostAddress,
    int HostControlPort,
    string HostDisplayName) : ControlMessage;

public record InvitationResponse(
    bool Accepted,
    string? Reason) : ControlMessage;

/// <summary>
/// Sent from Client → Host after the client has bound its UDP audio listener.
/// Lets the host fan-out audio to the client's actual ephemeral port rather than
/// assuming a fixed port — critical for same-machine local testing where host
/// and client would otherwise collide on UDP 5001.
/// </summary>
public record AudioClientReady(int ClientUdpPort) : ControlMessage;
