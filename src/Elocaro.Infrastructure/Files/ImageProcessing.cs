using SkiaSharp;

namespace Elocaro.Infrastructure.Files;

/// <summary>
/// Prepares photos for storage: applies the EXIF orientation, downscales to a maximum edge length and re-encodes.
/// Re-encoding removes all metadata (including the GPS position).
/// SkiaSharp cannot decode HEIC on Linux; such files are stored unchanged.
/// </summary>
internal static class ImageProcessing
{
    public static byte[]? Prepare(byte[] content, string mimeType, int maxEdge, int maxMegapixels, int jpegQuality)
    {
        var format = mimeType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png" => SKEncodedImageFormat.Png,
            _ => (SKEncodedImageFormat?)null,
        };
        if (format is null)
        {
            return null;
        }

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data)
            ?? throw new Application.Files.FileRejectedException("Das Bild ist beschädigt oder kann nicht gelesen werden.");
        if ((long)codec.Info.Width * codec.Info.Height > maxMegapixels * 1_000_000L)
        {
            throw new Application.Files.FileRejectedException($"Das Bild ist zu groß. Erlaubt sind höchstens {maxMegapixels} Megapixel.");
        }

        using var original = SKBitmap.Decode(codec)
            ?? throw new Application.Files.FileRejectedException("Das Bild ist beschädigt oder kann nicht gelesen werden.");

        using var oriented = Orient(original, codec.EncodedOrigin);
        using var resized = Shrink(oriented, maxEdge);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(format.Value, format == SKEncodedImageFormat.Jpeg ? jpegQuality : 100);
        return encoded.ToArray();
    }

    /// <summary>
    /// Applies the EXIF orientation (1–8). Each orientation is expressed as a source → target mapping,
    /// with w/h = width/height of the target image: x' = a·x + b·y + c, y' = d·x + e·y + f.
    /// </summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin orientation)
    {
        var swapped = orientation is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        float w = swapped ? source.Height : source.Width;
        float h = swapped ? source.Width : source.Height;

        SKMatrix? matrix = orientation switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // 2: mirrored horizontally
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // 3: rotated 180°
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // 4: mirrored vertically
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // 5: transposed
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),     // 6: 90° clockwise
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1), // 7: transverse
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),   // 8: 90° counterclockwise
            _ => null,
        };

        if (matrix is null)
        {
            return source.Copy();
        }

        var destination = new SKBitmap((int)w, (int)h, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(destination);
        canvas.SetMatrix(matrix.Value);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        canvas.Flush();
        return destination;
    }

    private static SKBitmap Shrink(SKBitmap source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge)
        {
            return source.Copy();
        }

        var factor = (double)maxEdge / longest;
        var info = new SKImageInfo((int)Math.Round(source.Width * factor), (int)Math.Round(source.Height * factor), source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Bild konnte nicht verkleinert werden.");
    }
}
