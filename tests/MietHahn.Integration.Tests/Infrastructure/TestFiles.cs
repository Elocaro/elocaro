using SkiaSharp;

namespace MietHahn.Integration.Tests.Infrastructure;

/// <summary>Erzeugt kleine Testdateien (PDF, Bilder mit EXIF-Ausrichtung).</summary>
internal static class TestFiles
{
    public static byte[] Pdf(string content = "Test") =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {content}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    /// <summary>Bild mit linker Hälfte rot und rechter Hälfte blau.</summary>
    public static byte[] TwoColorImage(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>Fügt einem JPEG direkt nach SOI ein APP1/Exif-Segment mit dem Orientation-Tag (0x0112) ein.</summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                          // APP1, Länge 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 0x08, // TIFF-Header (Big Endian), IFD0 bei Offset 8
            0x00, 0x01,                                       // 1 Eintrag
            0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1,              // Orientation, SHORT, Anzahl 1
            (byte)(orientation >> 8), (byte)orientation, 0, 0,
            0, 0, 0, 0,                                       // kein weiteres IFD
        ];

        return [.. jpeg.AsSpan(0, 2), .. exif, .. jpeg.AsSpan(2)];
    }

    public static SKColor Color(byte[] image, float xRatio, float yRatio)
    {
        using var bitmap = SKBitmap.Decode(image);
        return bitmap.GetPixel((int)(bitmap.Width * xRatio), (int)(bitmap.Height * yRatio));
    }

    public static (int Width, int Height) Size(byte[] image)
    {
        using var codec = SKCodec.Create(new MemoryStream(image));
        return (codec.Info.Width, codec.Info.Height);
    }

    public static bool IsRed(SKColor c) => c.Red > 180 && c.Blue < 80;

    public static bool IsBlue(SKColor c) => c.Blue > 180 && c.Red < 80;
}
