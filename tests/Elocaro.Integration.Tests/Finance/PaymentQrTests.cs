using Elocaro.Infrastructure.Common;

namespace Elocaro.Integration.Tests.Finance;

/// <summary>The Girocode (EPC-QR) follows the structure banking apps read: fixed lines, IBAN without spaces, amount with a point and two decimals.</summary>
public class PaymentQrTests
{
    private const string Iban = "DE89 3704 0044 0532 0130 00";

    [Fact]
    public void The_payload_has_the_fields_of_the_standard_in_order()
    {
        var payload = PaymentQr.Payload("Hans Vermieter", Iban, "COLSDE33XXX", 1234.5m, "Wohnung 1, Lindenweg 3")!;

        Assert.Equal(
            ["BCD", "002", "1", "SCT", "COLSDE33XXX", "Hans Vermieter", "DE89370400440532013000", "EUR1234.50", "", "", "Wohnung 1, Lindenweg 3", ""],
            payload.Split('\n'));
    }

    [Fact]
    public void The_bic_is_optional()
    {
        Assert.Equal("", PaymentQr.Payload("Hans Vermieter", Iban, null, 10m, "x")!.Split('\n')[4]);
        Assert.Equal("", PaymentQr.Payload("Hans Vermieter", Iban, "kaputt", 10m, "x")!.Split('\n')[4]);
    }

    [Theory]
    [InlineData("", Iban, 10)] // no recipient
    [InlineData("Hans", "DE00 0000 0000 0000 0000 00", 10)] // IBAN with a wrong checksum
    [InlineData("Hans", null, 10)]
    [InlineData("Hans", Iban, 0)]
    [InlineData("Hans", Iban, -5)]
    [InlineData("Hans", Iban, 0.004)]
    [InlineData("Hans", Iban, 1000000000)]
    public void No_code_is_made_when_no_valid_transfer_can_be_described(string recipient, string? iban, double amount)
    {
        Assert.Null(PaymentQr.Payload(recipient, iban, null, (decimal)amount, "x"));
        Assert.Null(PaymentQr.DataUri(recipient, iban, null, (decimal)amount, "x"));
    }

    [Fact]
    public void Long_texts_are_cut_and_line_breaks_cannot_shift_the_fields()
    {
        var payload = PaymentQr.Payload(new string('N', 100), Iban, null, 10m, "Zeile 1\nZeile 2\r\n" + new string('R', 200))!;
        var lines = payload.Split('\n');

        Assert.Equal(12, lines.Length);
        Assert.Equal(70, lines[5].Length);
        Assert.StartsWith("Zeile 1 Zeile 2 R", lines[10], StringComparison.Ordinal);
        Assert.Equal(140, lines[10].Length);
    }

    [Fact]
    public void The_whole_payload_stays_within_the_331_bytes_of_the_standard_even_with_umlauts()
    {
        var payload = PaymentQr.Payload(new string('Ü', 70), Iban, "COLSDE33XXX", 99999.99m, new string('ö', 140))!;

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload) <= 331);
        Assert.Equal(12, payload.Split('\n').Length);
    }

    [Fact]
    public void The_data_uri_is_an_svg_image_without_active_content()
    {
        var uri = PaymentQr.DataUri("Hans Vermieter", Iban, null, 42m, "Miete")!;

        Assert.StartsWith("data:image/svg+xml;base64,", uri, StringComparison.Ordinal);
        var svg = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
    }
}
