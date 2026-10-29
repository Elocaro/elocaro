using System.Security.Cryptography;

namespace Elocaro.TestHelpers;

/// <summary>TOTP per RFC 6238 (SHA-1, 30 s, 6 digits) – like authenticator apps, for testing two-factor sign-in.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset timestamp)
    {
        var key = Base32Decode(base32Key);
        var meter = timestamp.ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(meter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

#pragma warning disable CA5350 // SHA-1 is mandated for TOTP (RFC 6238)
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var result = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var chars in cleaned)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(chars, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. result];
    }
}
