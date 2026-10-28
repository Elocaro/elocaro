using System.Text;

namespace MietHahn.Infrastructure.Common;

/// <summary>Reads text files of unknown encoding; shared by the bank, receipt, meter and loan imports.</summary>
internal static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextDecoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>„auto“: UTF-8 (with or without BOM), otherwise Windows-1252, as German banks often deliver.</summary>
    public static string Read(byte[] content, string encoding = "auto")
    {
        ArgumentNullException.ThrowIfNull(content);
        var data = content.AsSpan();
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(data[3..]);
        }

        switch (encoding.ToLowerInvariant())
        {
            case "utf-8":
                return Encoding.UTF8.GetString(data);
            case "windows-1252":
                return Encoding.GetEncoding(1252).GetString(data);
            default:
                try
                {
                    return StrictUtf8.GetString(data);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.GetEncoding(1252).GetString(data);
                }
        }
    }
}
