using SkiaSharp;

namespace MietHahn.Infrastructure.Dateien;

/// <summary>
/// Bereitet Fotos für die Ablage auf: EXIF-Ausrichtung anwenden, auf eine Maximalkante verkleinern und neu kodieren.
/// Durch das Neukodieren werden alle Metadaten (inkl. GPS-Position) entfernt.
/// HEIC kann SkiaSharp unter Linux nicht dekodieren; solche Dateien werden unverändert abgelegt.
/// </summary>
internal static class BildAufbereitung
{
    public static byte[]? Aufbereiten(byte[] inhalt, string mimeTyp, int maxKante, int jpegQualitaet)
    {
        var format = mimeTyp switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            _ => (SKEncodedImageFormat?)null,
        };
        if (format is null)
        {
            return null;
        }

        using var daten = SKData.CreateCopy(inhalt);
        using var codec = SKCodec.Create(daten)
            ?? throw new Application.Dateien.DateiAbgelehntException("Das Bild ist beschädigt oder kann nicht gelesen werden.");
        using var original = SKBitmap.Decode(codec)
            ?? throw new Application.Dateien.DateiAbgelehntException("Das Bild ist beschädigt oder kann nicht gelesen werden.");

        using var ausgerichtet = Ausrichten(original, codec.EncodedOrigin);
        using var verkleinert = Verkleinern(ausgerichtet, maxKante);
        using var bild = SKImage.FromBitmap(verkleinert);
        using var kodiert = bild.Encode(format.Value, format == SKEncodedImageFormat.Jpeg ? jpegQualitaet : 100);
        return kodiert.ToArray();
    }

    /// <summary>
    /// Wendet die EXIF-Ausrichtung (1–8) an. Jede Orientierung ist als Abbildung Quelle → Ziel formuliert,
    /// mit w/h = Breite/Höhe des Zielbilds: x' = a·x + b·y + c, y' = d·x + e·y + f.
    /// </summary>
    private static SKBitmap Ausrichten(SKBitmap quelle, SKEncodedOrigin ausrichtung)
    {
        var vertauscht = ausrichtung is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        float w = vertauscht ? quelle.Height : quelle.Width;
        float h = vertauscht ? quelle.Width : quelle.Height;

        SKMatrix? matrix = ausrichtung switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // 2: horizontal gespiegelt
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // 3: 180° gedreht
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // 4: vertikal gespiegelt
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // 5: transponiert
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),     // 6: 90° im Uhrzeigersinn
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1), // 7: transversal
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),   // 8: 90° gegen den Uhrzeigersinn
            _ => null,
        };

        if (matrix is null)
        {
            return quelle.Copy();
        }

        var ziel = new SKBitmap((int)w, (int)h, quelle.ColorType, quelle.AlphaType);
        using var leinwand = new SKCanvas(ziel);
        leinwand.SetMatrix(matrix.Value);
        using var bild = SKImage.FromBitmap(quelle);
        leinwand.DrawImage(bild, 0, 0, SKSamplingOptions.Default);
        leinwand.Flush();
        return ziel;
    }

    private static SKBitmap Verkleinern(SKBitmap quelle, int maxKante)
    {
        var laengste = Math.Max(quelle.Width, quelle.Height);
        if (laengste <= maxKante)
        {
            return quelle.Copy();
        }

        var faktor = (double)maxKante / laengste;
        var info = new SKImageInfo((int)Math.Round(quelle.Width * faktor), (int)Math.Round(quelle.Height * faktor), quelle.ColorType, quelle.AlphaType);
        return quelle.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Bild konnte nicht verkleinert werden.");
    }
}
