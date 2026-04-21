using System.Text.Json.Serialization;

namespace LiveStreamSound.Shared.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(AuthFail), "authFail")]
[JsonDerivedType(typeof(SetVolume), "setVolume")]
[JsonDerivedType(typeof(SetMute), "setMute")]
[JsonDerivedType(typeof(ListOutputDevicesRequest), "listOutputDevices")]
[JsonDerivedType(typeof(OutputDevicesResponse), "outputDevices")]
[JsonDerivedType(typeof(SetOutputDevice), "setOutputDevice")]
[JsonDerivedType(typeof(Kick), "kick")]
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Pong), "pong")]
[JsonDerivedType(typeof(ClientStatus), "clientStatus")]
[JsonDerivedType(typeof(SessionEnding), "sessionEnding")]
[JsonDerivedType(typeof(Invitation), "invitation")]
[JsonDerivedType(typeof(InvitationResponse), "invitationResponse")]
[JsonDerivedType(typeof(AudioClientReady), "audioClientReady")]
public abstract record ControlMessage;
