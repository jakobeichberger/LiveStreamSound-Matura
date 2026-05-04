using System.Security.Cryptography;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.Shared.Tests;

public class SessionCryptoTests
{
    private const string Code = "428193";

    [Fact]
    public void Derive_SameInputs_ProducesIdenticalKey_AcrossInstances()
    {
        var salt = SessionCrypto.GenerateSalt();
        var a = SessionCrypto.Derive(Code, salt);
        var b = SessionCrypto.Derive(Code, salt);

        // Indirect equivalence test: encrypt with `a`, decrypt with `b`.
        var plaintext = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SessionCrypto.TagSizeBytes];
        a.EncryptAudio(42, plaintext, ciphertext, tag);

        var roundtripped = new byte[plaintext.Length];
        b.DecryptAudio(42, ciphertext, tag, roundtripped);
        Assert.Equal(plaintext, roundtripped);
    }

    [Fact]
    public void Derive_DifferentSalt_ProducesDifferentKey()
    {
        var salt1 = SessionCrypto.GenerateSalt();
        var salt2 = SessionCrypto.GenerateSalt();
        var a = SessionCrypto.Derive(Code, salt1);
        var b = SessionCrypto.Derive(Code, salt2);

        var plaintext = new byte[] { 9, 8, 7 };
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SessionCrypto.TagSizeBytes];
        a.EncryptAudio(1, plaintext, ciphertext, tag);

        var roundtripped = new byte[plaintext.Length];
        Assert.ThrowsAny<CryptographicException>(() =>
            b.DecryptAudio(1, ciphertext, tag, roundtripped));
    }

    [Fact]
    public void Derive_DifferentCode_ProducesDifferentKey()
    {
        var salt = SessionCrypto.GenerateSalt();
        var a = SessionCrypto.Derive("000001", salt);
        var b = SessionCrypto.Derive("999999", salt);

        var plaintext = new byte[] { 1, 2, 3 };
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SessionCrypto.TagSizeBytes];
        a.EncryptAudio(1, plaintext, ciphertext, tag);

        var roundtripped = new byte[plaintext.Length];
        Assert.ThrowsAny<CryptographicException>(() =>
            b.DecryptAudio(1, ciphertext, tag, roundtripped));
    }

    [Fact]
    public void Encrypt_SameSequence_DifferentPayload_ProducesDifferentCiphertext()
    {
        var salt = SessionCrypto.GenerateSalt();
        var crypto = SessionCrypto.Derive(Code, salt);

        var p1 = new byte[] { 1, 1, 1, 1 };
        var p2 = new byte[] { 2, 2, 2, 2 };
        var c1 = new byte[4]; var t1 = new byte[16];
        var c2 = new byte[4]; var t2 = new byte[16];

        crypto.EncryptAudio(1, p1, c1, t1);
        crypto.EncryptAudio(1, p2, c2, t2);

        Assert.NotEqual(c1, c2);
        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var salt = SessionCrypto.GenerateSalt();
        var crypto = SessionCrypto.Derive(Code, salt);
        var plaintext = new byte[] { 4, 5, 6, 7 };
        var ciphertext = new byte[4];
        var tag = new byte[16];
        crypto.EncryptAudio(7, plaintext, ciphertext, tag);

        // Flip a single bit in ciphertext.
        ciphertext[2] ^= 0x01;
        var dest = new byte[4];
        Assert.ThrowsAny<CryptographicException>(() => crypto.DecryptAudio(7, ciphertext, tag, dest));
    }

    [Fact]
    public void Decrypt_WrongSequence_Throws()
    {
        var salt = SessionCrypto.GenerateSalt();
        var crypto = SessionCrypto.Derive(Code, salt);
        var plaintext = new byte[] { 1, 2, 3 };
        var ciphertext = new byte[3];
        var tag = new byte[16];
        crypto.EncryptAudio(100, plaintext, ciphertext, tag);

        var dest = new byte[3];
        Assert.ThrowsAny<CryptographicException>(() => crypto.DecryptAudio(101, ciphertext, tag, dest));
    }

    [Fact]
    public void Mac_DeterministicForSameInput()
    {
        var salt = SessionCrypto.GenerateSalt();
        var crypto = SessionCrypto.Derive(Code, salt);
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var m1 = crypto.Mac(data);
        var m2 = crypto.Mac(data);
        Assert.Equal(m1, m2);
        Assert.Equal(16, m1.Length); // truncated to 16 bytes
    }

    [Fact]
    public void VerifyMac_DetectsTamper()
    {
        var salt = SessionCrypto.GenerateSalt();
        var crypto = SessionCrypto.Derive(Code, salt);
        var data = new byte[] { 1, 2, 3 };
        var mac = crypto.Mac(data);
        Assert.True(crypto.VerifyMac(data, mac));

        // Flip one byte of data.
        data[1] ^= 0x01;
        Assert.False(crypto.VerifyMac(data, mac));
    }

    [Fact]
    public void GenerateSalt_ReturnsRandomDistinctValues()
    {
        var s1 = SessionCrypto.GenerateSalt();
        var s2 = SessionCrypto.GenerateSalt();
        Assert.Equal(SessionCrypto.SaltSizeBytes, s1.Length);
        Assert.Equal(SessionCrypto.SaltSizeBytes, s2.Length);
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Hex_RoundTripsCorrectly()
    {
        var bytes = new byte[] { 0x00, 0x12, 0xAB, 0xFF };
        var hex = SessionCrypto.ToHex(bytes);
        Assert.Equal("0012abff", hex);
        var parsed = SessionCrypto.FromHex(hex);
        Assert.NotNull(parsed);
        Assert.Equal(bytes, parsed);
    }

    [Fact]
    public void FromHex_RejectsMalformed()
    {
        Assert.Null(SessionCrypto.FromHex("abc")); // odd length
        Assert.Null(SessionCrypto.FromHex(null));
        Assert.Null(SessionCrypto.FromHex(""));
        Assert.Null(SessionCrypto.FromHex("zzzz"));
    }

    [Fact]
    public void CanonicalWelcomeBytes_StableAcrossCalls()
    {
        var a = SessionCrypto.CanonicalWelcomeBytes("id", 5001, 48000, 2, "opus", 1234, "deadbeef");
        var b = SessionCrypto.CanonicalWelcomeBytes("id", 5001, 48000, 2, "opus", 1234, "deadbeef");
        Assert.Equal(a, b);
    }
}
