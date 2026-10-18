using System.Text;

namespace MietHahn.Infrastructure.Dateien;

/// <summary>Erlaubte Dateitypen, erkannt am Inhalt (Magic Bytes), nicht an der Endung.</summary>
internal static class DateiTypErkennung
{
    public const int BenoetigteBytes = 16;

    private static readonly string[] HeifMarken = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

    private static ReadOnlySpan<byte> JpegSignatur => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignatur => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ErkannterTyp? Erkennen(ReadOnlySpan<byte> kopf)
    {
        if (kopf.StartsWith("%PDF-"u8))
        {
            return new ErkannterTyp("application/pdf", ".pdf", IstBild: false);
        }

        if (kopf.StartsWith(JpegSignatur))
        {
            return new ErkannterTyp("image/jpeg", ".jpg", IstBild: true);
        }

        if (kopf.StartsWith(PngSignatur))
        {
            return new ErkannterTyp("image/png", ".png", IstBild: true);
        }

        // ISO-BMFF: Bytes 4–7 "ftyp", danach die Hauptmarke.
        if (kopf.Length >= 12 && kopf[4..8].SequenceEqual("ftyp"u8))
        {
            var marke = Encoding.ASCII.GetString(kopf[8..12]);
            if (HeifMarken.Contains(marke, StringComparer.Ordinal))
            {
                return new ErkannterTyp("image/heic", ".heic", IstBild: true);
            }
        }

        return null;
    }
}

internal sealed record ErkannterTyp(string MimeTyp, string Endung, bool IstBild);
