using System.Text;
using QRCoder;

namespace Elocaro.Infrastructure.Statements;

/// <summary>
/// QR code in the statement letter that leads to the tenant portal. The code holds only the plain portal link: no token, no
/// personal data; the tenant still has to sign in.
/// </summary>
internal static class PortalLinkQr
{
    /// <summary>SVG of the code as a data URI for an <c>img</c> element.</summary>
    public static string DataUri(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link.AbsoluteUri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(4);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
}
