using System.Security.Cryptography;

namespace MietHahn.TestHilfen;

/// <summary>TOTP nach RFC 6238 (SHA-1, 30 s, 6 Ziffern) – wie Authenticator-Apps, für Tests der Zwei-Faktor-Anmeldung.</summary>
public static class Totp
{
    public static string Code(string base32Schluessel, DateTimeOffset zeitpunkt)
    {
        var schluessel = Base32Dekodieren(base32Schluessel);
        var zaehler = zeitpunkt.ToUnixTimeSeconds() / 30;
        var nachricht = BitConverter.GetBytes(zaehler);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(nachricht);
        }

#pragma warning disable CA5350 // SHA-1 ist für TOTP (RFC 6238) vorgeschrieben
        var hash = HMACSHA1.HashData(schluessel, nachricht);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binaer = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binaer % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Dekodieren(string eingabe)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bereinigt = eingabe.Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var ergebnis = new List<byte>();
        int puffer = 0, bits = 0;
        foreach (var zeichen in bereinigt)
        {
            puffer = (puffer << 5) | Alphabet.IndexOf(zeichen, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                ergebnis.Add((byte)(puffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. ergebnis];
    }
}
