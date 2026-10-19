using System.Text;

namespace MietHahn.Infrastructure.Files;

/// <summary>Erlaubte Dateitypen, erkannt am Inhalt (Magic Bytes), nicht an der Endung.</summary>
internal static class FileTypeDetection
{
    public const int RequiredBytes = 16;

    private static readonly string[] HeifBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static DetectedType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("%PDF-"u8))
        {
            return new DetectedType("application/pdf", ".pdf", IsImage: false);
        }

        if (header.StartsWith(JpegSignature))
        {
            return new DetectedType("image/jpeg", ".jpg", IsImage: true);
        }

        if (header.StartsWith(PngSignature))
        {
            return new DetectedType("image/png", ".png", IsImage: true);
        }

        // ISO-BMFF: Bytes 4–7 "ftyp", danach die Hauptmarke.
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(header[8..12]);
            if (HeifBrands.Contains(brand, StringComparer.Ordinal))
            {
                return new DetectedType("image/heic", ".heic", IsImage: true);
            }
        }

        return null;
    }
}

internal sealed record DetectedType(string MimeType, string Extension, bool IsImage);
