using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>
/// Encrypts the content of the emergency folder into one self-contained HTML file. Only standard building blocks are used:
/// the key comes from the password via PBKDF2-SHA256 (random salt), the content is sealed with AES-256-GCM (random nonce).
/// The parameters are stored in plain text in the file, so the browser can decrypt it with WebCrypto, offline and without Elocaro.
/// </summary>
internal static class EmergencyFolderEncryption
{
    /// <summary>OWASP minimum for PBKDF2-HMAC-SHA256.</summary>
    public const int Iterations = 600_000;

    public const int SaltLength = 16;

    /// <summary>96 bits, the length GCM is designed for.</summary>
    public const int NonceLength = 12;

    public const int KeyLength = 32;

    public const int TagLength = 16;

    /// <summary>Authenticated but not secret: ties the ciphertext to this file format.</summary>
    public const string AssociatedData = "elocaro-notfallmappe-v1";

    public const string DataElementId = "mappe-daten";

    /// <summary>Encrypts <paramref name="plaintext"/> and returns the complete HTML file (UTF-8).</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password, DateOnly createdOn)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var sealedData = new byte[plaintext.Length + TagLength];
        var key = DeriveKey(password, salt, Iterations);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, sealedData.AsSpan(0, plaintext.Length), sealedData.AsSpan(plaintext.Length, TagLength), Encoding.ASCII.GetBytes(AssociatedData));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return Page(salt, nonce, sealedData, createdOn);
    }

    /// <summary>The same key derivation as in the browser: UTF-8 of the NFC-normalised password.</summary>
    internal static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, KeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] Page(byte[] salt, byte[] nonce, byte[] sealedData, DateOnly createdOn)
    {
        // Everything between the markers is plain ASCII (Base64 and JSON punctuation), so it cannot close the script element.
        var header = new StringBuilder()
            .Append("{\"format\":\"elocaro-notfallmappe\",\"version\":1,\"algorithm\":\"AES-256-GCM\",\"kdf\":\"PBKDF2-SHA256\",\"iterations\":")
            .Append(Iterations.ToString(CultureInfo.InvariantCulture))
            .Append(",\"salt\":\"").Append(Convert.ToBase64String(salt))
            .Append("\",\"nonce\":\"").Append(Convert.ToBase64String(nonce))
            .Append("\",\"aad\":\"").Append(AssociatedData)
            .Append("\",\"ciphertext\":\"")
            .ToString();

        var template = UnlockPage.Template(createdOn);
        var marker = "{{DATA}}";
        var split = template.IndexOf(marker, StringComparison.Ordinal);
        var before = Encoding.UTF8.GetBytes(template[..split] + header);
        var after = Encoding.UTF8.GetBytes("\"}" + template[(split + marker.Length)..]);

        // Base64 without line breaks has an exact length, so the buffer is returned as it is (no second copy of the folder).
        var encodedLength = Base64.GetMaxEncodedToUtf8Length(sealedData.Length);
        var result = new byte[before.Length + encodedLength + after.Length];
        before.CopyTo(result, 0);
        Base64.EncodeToUtf8(sealedData, result.AsSpan(before.Length, encodedLength), out _, out var written);
        System.Diagnostics.Debug.Assert(written == encodedLength, "Base64 length must be exact");
        after.CopyTo(result, before.Length + written);
        return result;
    }
}
