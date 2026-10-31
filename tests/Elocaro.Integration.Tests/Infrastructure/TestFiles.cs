using SkiaSharp;

namespace Elocaro.Integration.Tests.Infrastructure;

/// <summary>Creates small test files (PDF, images with EXIF orientation).</summary>
internal static class TestFiles
{
    public static byte[] Pdf(string content = "Test") =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {content}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    /// <summary>Image with the left half red and the right half blue.</summary>
    /// <summary>A tiny PNG that declares the given dimensions in its header; its image data is far too short for them.</summary>
    public static byte[] PngWithDimensions(int width, int height)
    {
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk("IHDR", header);
        WriteChunk("IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        WriteChunk("IEND", []);
        return png.ToArray();

        void WriteChunk(string type, byte[] data)
        {
            byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
            var crc = uint.MaxValue;
            foreach (var value in typed)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
            }

            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            png.Write(number);
            png.Write(typed);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            png.Write(number);
        }
    }

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

    /// <summary>Inserts an APP1/Exif segment with the Orientation tag (0x0112) into a JPEG directly after SOI.</summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                          // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 0x08, // TIFF header (big endian), IFD0 at offset 8
            0x00, 0x01,                                       // 1 entry
            0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1,              // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0, 0,
            0, 0, 0, 0,                                       // no further IFD
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
