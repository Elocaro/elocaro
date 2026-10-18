using SkiaSharp;

namespace MietHahn.Integration.Tests.Infrastruktur;

/// <summary>Erzeugt kleine Testdateien (PDF, Bilder mit EXIF-Ausrichtung).</summary>
internal static class TestDateien
{
    public static byte[] Pdf(string inhalt = "Test") =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {inhalt}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    /// <summary>Bild mit linker Hälfte rot und rechter Hälfte blau.</summary>
    public static byte[] ZweifarbigesBild(int breite, int hoehe, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(breite, hoehe);
        using (var leinwand = new SKCanvas(bitmap))
        {
            using var rot = new SKPaint { Color = SKColors.Red };
            using var blau = new SKPaint { Color = SKColors.Blue };
            leinwand.DrawRect(0, 0, breite / 2f, hoehe, rot);
            leinwand.DrawRect(breite / 2f, 0, breite / 2f, hoehe, blau);
        }

        using var bild = SKImage.FromBitmap(bitmap);
        using var daten = bild.Encode(format, 95);
        return daten.ToArray();
    }

    /// <summary>Fügt einem JPEG direkt nach SOI ein APP1/Exif-Segment mit dem Orientation-Tag (0x0112) ein.</summary>
    public static byte[] MitExifAusrichtung(byte[] jpeg, ushort ausrichtung)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                          // APP1, Länge 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 0x08, // TIFF-Header (Big Endian), IFD0 bei Offset 8
            0x00, 0x01,                                       // 1 Eintrag
            0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1,              // Orientation, SHORT, Anzahl 1
            (byte)(ausrichtung >> 8), (byte)ausrichtung, 0, 0,
            0, 0, 0, 0,                                       // kein weiteres IFD
        ];

        return [.. jpeg.AsSpan(0, 2), .. exif, .. jpeg.AsSpan(2)];
    }

    public static SKColor Farbe(byte[] bild, float xAnteil, float yAnteil)
    {
        using var bitmap = SKBitmap.Decode(bild);
        return bitmap.GetPixel((int)(bitmap.Width * xAnteil), (int)(bitmap.Height * yAnteil));
    }

    public static (int Breite, int Hoehe) Groesse(byte[] bild)
    {
        using var codec = SKCodec.Create(new MemoryStream(bild));
        return (codec.Info.Width, codec.Info.Height);
    }

    public static bool IstRot(SKColor c) => c.Red > 180 && c.Blue < 80;

    public static bool IstBlau(SKColor c) => c.Blue > 180 && c.Red < 80;
}
