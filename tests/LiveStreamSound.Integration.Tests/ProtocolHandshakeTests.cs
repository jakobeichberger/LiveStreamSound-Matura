using System.Net;
using System.Net.Sockets;
using System.Text;
using LiveStreamSound.Host.Services;
using LiveStreamSound.Shared.Discovery;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Integration.Tests;

/// <summary>
/// End-to-end tests that spin up a real <see cref="ControlServer"/> in-process
/// and connect to it via raw TCP, asserting protocol invariants without the
/// audio pipeline (which requires WASAPI / a Windows audio device that the
/// CI runner may not have).
///
/// <para>
/// These guard against regressions that unit tests can miss — protocol-version
/// rejection, AUTH_FAIL response shape, Welcome MAC verifiability end-to-end.
/// </para>
/// </summary>
[Collection("HostTcpServer")]
public class ProtocolHandshakeTests
{
    [Fact]
    public async Task FullHandshake_HappyPath_ProducesVerifiableWelcomeMac()
    {
        using var log = new LogService("LiveStreamSound-IntegrationTest");
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);

        var code = sessions.StartSession();
        control.Start(preferredPort: 0); // ephemeral port
        var port = control.Port;

        // Client side: open a real TCP socket, send HELLO, read WELCOME, verify MAC.
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();

        var hello = new Hello(code, "Raum 99", DiscoveryConstants.ProtocolVersion);
        await MessageJson.WriteFrameAsync(stream, hello);

        var reply = await MessageJson.ReadFrameAsync(stream);
        var welcome = Assert.IsType<Welcome>(reply);

        // Verify the MAC using a client-side derivation matching what the
        // host did — this proves the cryptographic handshake works end-to-end.
        var salt = SessionCrypto.FromHex(welcome.SessionSaltHex);
        var mac = SessionCrypto.FromHex(welcome.WelcomeMacHex);
        Assert.NotNull(salt);
        Assert.NotNull(mac);
        var clientCrypto = SessionCrypto.Derive(code, salt!);
        var canonical = SessionCrypto.CanonicalWelcomeBytes(
            welcome.ClientId, welcome.AudioUdpPort, welcome.SampleRate,
            welcome.Channels, welcome.AudioCodec, welcome.ServerTimeMs,
            welcome.SessionSaltHex);
        Assert.True(clientCrypto.VerifyMac(canonical, mac!));
    }

    [Fact]
    public async Task ProtocolVersionMismatch_ProducesAuthFail_WithSpecificReason()
    {
        using var log = new LogService("LiveStreamSound-IntegrationTest");
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);
        var code = sessions.StartSession();
        control.Start(preferredPort: 0);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, control.Port);
        var stream = tcp.GetStream();

        // Send v=99 — guaranteed mismatch.
        await MessageJson.WriteFrameAsync(stream, new Hello(code, "Raum 99", 99));
        var reply = await MessageJson.ReadFrameAsync(stream);
        var fail = Assert.IsType<AuthFail>(reply);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", fail.Reason);
    }

    [Fact]
    public async Task WrongCode_ProducesAuthFail_WithUnifiedReason()
    {
        using var log = new LogService("LiveStreamSound-IntegrationTest");
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);
        sessions.StartSession();
        control.Start(preferredPort: 0);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, control.Port);
        var stream = tcp.GetStream();

        // Send a wrong code with the right protocol version — should get
        // unified AUTH_FAILED (no enumeration leak).
        await MessageJson.WriteFrameAsync(stream,
            new Hello("000000", "Raum 99", DiscoveryConstants.ProtocolVersion));
        var reply = await MessageJson.ReadFrameAsync(stream);
        var fail = Assert.IsType<AuthFail>(reply);
        Assert.Equal("AUTH_FAILED", fail.Reason);
    }

    [Fact]
    public async Task NoSession_ProducesSameAuthFailedReason_NoEnumeration()
    {
        using var log = new LogService("LiveStreamSound-IntegrationTest");
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);
        // Notably DO NOT start the session.
        control.Start(preferredPort: 0);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, control.Port);
        var stream = tcp.GetStream();

        await MessageJson.WriteFrameAsync(stream,
            new Hello("123456", "Raum 99", DiscoveryConstants.ProtocolVersion));
        var reply = await MessageJson.ReadFrameAsync(stream);
        var fail = Assert.IsType<AuthFail>(reply);
        // Same opaque reason as wrong-code so an attacker can't tell the
        // session-active-vs-stale state from the wire.
        Assert.Equal("AUTH_FAILED", fail.Reason);
    }

    [Fact]
    public async Task NonHelloFirstMessage_ServerCloses()
    {
        using var log = new LogService("LiveStreamSound-IntegrationTest");
        using var sessions = new SessionManager(log);
        await using var control = new ControlServer(sessions, log);
        sessions.StartSession();
        control.Start(preferredPort: 0);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, control.Port);
        var stream = tcp.GetStream();

        // Send something that's not HELLO — server should close without reply.
        await MessageJson.WriteFrameAsync(stream, new Ping(0));

        // Reading should hit EOF (null frame).
        var reply = await MessageJson.ReadFrameAsync(stream);
        Assert.Null(reply);
    }
}

[CollectionDefinition("HostTcpServer", DisableParallelization = true)]
public sealed class HostTcpServerCollection { }
