using System.Globalization;
using System.Text;
using Elocaro.Domain.Common;
using QRCoder;

namespace Elocaro.Infrastructure.Common;

/// <summary>
/// The Girocode (EPC-QR, "BCD" version 002) for a credit transfer: banking apps read recipient, IBAN, amount and reference from it
/// and fill the transfer form. It holds only what the letter prints next to it anyway (bank details of the landlord, amount, reference).
/// </summary>
internal static class PaymentQr
{
    private const int MaxName = 70;
    private const int MaxReference = 140;
    private const decimal MaxAmount = 999_999_999.99m;
    /// <summary>The standard allows 331 bytes in all; with many umlauts the reference is cut further to stay under it.</summary>
    private const int MaxBytes = 331;

    /// <summary>The text of the code; <c>null</c> if no valid transfer can be described (no recipient, invalid IBAN, amount not between 0.01 and 999,999,999.99 euros).</summary>
    public static string? Payload(string? recipient, string? iban, string? bic, decimal amount, string? reference)
    {
        var name = OneLine(recipient, MaxName);
        if (name.Length == 0 || !Iban.IsValid(iban) || amount < 0.01m || amount > MaxAmount)
        {
            return null;
        }

        var bicCompact = string.IsNullOrWhiteSpace(bic) ? "" : new string(bic.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (bicCompact.Length is not (0 or 8 or 11) || !bicCompact.All(char.IsAsciiLetterOrDigit))
        {
            bicCompact = ""; // the BIC is optional (version 002), a wrong one would only make the app reject the code
        }

        var text = OneLine(reference, MaxReference);
        string Build() =>
            // Service tag, version, character set 1 = UTF-8, identification SCT = SEPA credit transfer, then the fields of the standard.
            string.Join('\n',
                "BCD", "002", "1", "SCT",
                bicCompact,
                name,
                Iban.Compact(iban!),
                "EUR" + decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture),
                "", // purpose code
                "", // structured reference (an unstructured text follows)
                text,
                ""); // information for the payer

        var payload = Build();
        while (Encoding.UTF8.GetByteCount(payload) > MaxBytes && text.Length > 0)
        {
            text = text[..^1];
            payload = Build();
        }

        return Encoding.UTF8.GetByteCount(payload) > MaxBytes ? null : payload;
    }

    /// <summary>SVG of the code as a data URI for an <c>img</c> element; <c>null</c> if <see cref="Payload"/> is.</summary>
    public static string? DataUri(string? recipient, string? iban, string? bic, decimal amount, string? reference)
    {
        if (Payload(recipient, iban, bic, amount, reference) is not { } payload)
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var svg = new SvgQRCode(data).GetGraphic(4);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>One line without control characters, at most <paramref name="max"/> characters.</summary>
    private static string OneLine(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length > max ? text[..max] : text;
    }
}
