using System.IO;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// Regression for the "UNEXPECTED_RESPONSE on connect" bug — verifies that a
/// Welcome message with crypto fields round-trips through serialization AND
/// the MAC computed on one side verifies on the other. Reproduces the full
/// in-memory handshake without needing TCP / Windows runtime.
/// </summary>
public class WelcomeHandshakeTests
{
    private const string Code = "428193";

    [Fact]
    public async Task FullWelcomeHandshake_RoundTripsViaJson_AndMacVerifies()
    {
        // ------------- HOST SIDE -------------
        var serverSalt = SessionCrypto.GenerateSalt();
        var serverSaltHex = SessionCrypto.ToHex(serverSalt);
        var serverCrypto = SessionCrypto.Derive(Code, serverSalt);

        const string clientId = "abc123def456";
        const int audioPort = 5001;
        const long serverTimeMs = 1_700_000_000_000L;

        var canonical = SessionCrypto.CanonicalWelcomeBytes(
            clientId, audioPort, 48000, 2, "opus", serverTimeMs, serverSaltHex);
        var serverMacHex = SessionCrypto.ToHex(serverCrypto.Mac(canonical));

        var welcomeFromServer = new Welcome(
            ClientId: clientId,
            AudioUdpPort: audioPort,
            SampleRate: 48000,
            Channels: 2,
            AudioCodec: "opus",
            ServerTimeMs: serverTimeMs,
            SessionSaltHex: serverSaltHex,
            WelcomeMacHex: serverMacHex);

        // ------------- WIRE TRANSPORT -------------
        using var ms = new MemoryStream();
        await MessageJson.WriteFrameAsync(ms, welcomeFromServer);
        ms.Position = 0;

        // ------------- CLIENT SIDE -------------
        var received = await MessageJson.ReadFrameAsync(ms);
        Assert.NotNull(received);
        var welcome = Assert.IsType<Welcome>(received);

        // All fields survive the round trip.
        Assert.Equal(clientId, welcome.ClientId);
        Assert.Equal(audioPort, welcome.AudioUdpPort);
        Assert.Equal(serverSaltHex, welcome.SessionSaltHex);
        Assert.Equal(serverMacHex, welcome.WelcomeMacHex);

        // Client derives crypto + verifies MAC.
        var salt = SessionCrypto.FromHex(welcome.SessionSaltHex);
        var mac = SessionCrypto.FromHex(welcome.WelcomeMacHex);
        Assert.NotNull(salt);
        Assert.NotNull(mac);
        Assert.True(salt!.Length >= 8, "Salt must be at least 8 bytes after hex decode");

        var clientCrypto = SessionCrypto.Derive(Code, salt!);
        var clientCanonical = SessionCrypto.CanonicalWelcomeBytes(
            welcome.ClientId, welcome.AudioUdpPort, welcome.SampleRate,
            welcome.Channels, welcome.AudioCodec, welcome.ServerTimeMs,
            welcome.SessionSaltHex);

        // Bytes must match exactly across the wire — any mismatch breaks the MAC.
        Assert.Equal(canonical, clientCanonical);
        Assert.True(clientCrypto.VerifyMac(clientCanonical, mac!),
            "Welcome MAC verification should succeed when both sides use the same code+salt");
    }

    [Fact]
    public async Task Welcome_DeserializesAsWelcome_NotSomethingElse()
    {
        // Specifically guards against the "UNEXPECTED_RESPONSE" bug where the
        // client's switch hits the default case because the response wasn't
        // recognised as Welcome.
        var welcome = new Welcome("x", 5001, 48000, 2, "opus", 0, "deadbeef00112233", "00112233445566778899aabbccddeeff");
        using var ms = new MemoryStream();
        await MessageJson.WriteFrameAsync(ms, welcome);
        ms.Position = 0;

        var received = await MessageJson.ReadFrameAsync(ms);
        Assert.IsType<Welcome>(received);
    }

    [Fact]
    public async Task Welcome_FromOlderHostWithoutSaltOrMac_ParsesAsWelcomeWithNullFields()
    {
        // What happens if a v1 host (no AEAD) responds to a v2 client. The
        // JSON would be missing SessionSaltHex/WelcomeMacHex. The client's
        // code path should explicitly handle this and produce a clear error
        // (AUTH_FAIL:WELCOME_MALFORMED), NOT a confusing UNEXPECTED_RESPONSE.
        const string v1Json = "{\"type\":\"welcome\"," +
            "\"clientId\":\"x\",\"audioUdpPort\":5001,\"sampleRate\":48000," +
            "\"channels\":2,\"audioCodec\":\"opus\",\"serverTimeMs\":0}";
        var lengthPrefix = new byte[]
        {
            (byte)((v1Json.Length >> 24) & 0xFF),
            (byte)((v1Json.Length >> 16) & 0xFF),
            (byte)((v1Json.Length >> 8) & 0xFF),
            (byte)(v1Json.Length & 0xFF),
        };
        using var ms = new MemoryStream();
        ms.Write(lengthPrefix);
        ms.Write(System.Text.Encoding.UTF8.GetBytes(v1Json));
        ms.Position = 0;

        var received = await MessageJson.ReadFrameAsync(ms);
        // Whatever happens — null OR Welcome with null sub-fields — we MUST
        // be able to detect the situation cleanly. Verify it deserializes
        // into Welcome (the framework's behavior is to default-init missing
        // fields, not fail outright).
        Assert.IsType<Welcome>(received);
        var w = (Welcome)received!;
        // The required-but-missing properties default to null with
        // System.Text.Json record positional ctor handling.
        // → Client code path must check both for null and report
        //   AUTH_FAIL:WELCOME_MALFORMED, not crash or silently fall through
        //   to UNEXPECTED_RESPONSE.
        Assert.True(w.SessionSaltHex is null,
            "Sanity check that the framework actually leaves missing fields null");
    }
}
