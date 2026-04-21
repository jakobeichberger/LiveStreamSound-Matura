using System.Security.Cryptography;

namespace LiveStreamSound.Shared.Session;

public static class SessionCode
{
    public const int Digits = 6;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var number = BitConverter.ToUInt32(bytes) % 1_000_000u;
        return number.ToString("D6");
    }

    public static bool IsValidFormat(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Digits)
            return false;
        foreach (var c in code)
        {
            if (c < '0' || c > '9') return false;
        }
        return true;
    }
}
