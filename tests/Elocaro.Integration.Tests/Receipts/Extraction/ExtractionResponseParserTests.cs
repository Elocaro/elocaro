using System.Text.Json;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Receipts.Extraction;

namespace Elocaro.Integration.Tests.Receipts.Extraction;

public class ExtractionResponseParserTests
{
    private const string Complete = """
        {
          "supplier_name": {"value": "Grünpflege Meier GmbH", "confidence": 0.97},
          "supplier_iban": {"value": "DE89 3704 0044 0532 0130 00", "confidence": 0.9},
          "invoice_number": {"value": "R-2026-117", "confidence": 0.99},
          "invoice_date": {"value": "2026-04-30", "confidence": 0.98},
          "service_from": {"value": "2026-01-01", "confidence": 0.8},
          "service_to": {"value": "2026-03-31", "confidence": 0.8},
          "due_date": {"value": "2026-05-14", "confidence": 0.9},
          "gross": {"value": 595.00, "confidence": 0.99},
          "net": {"value": 500, "confidence": 0.99},
          "vat": {"value": 95, "confidence": 0.99},
          "cost_type": {"value": "betrkv-10", "confidence": 0.6},
          "section_35a": {"value": "tradesman_service", "confidence": 0.7},
          "labor_costs": {"value": 357, "confidence": 0.7},
          "fuel_quantity": {"value": null, "confidence": 0},
          "lines": [{"description": "Rasenpflege", "amount": 500, "cost_type": "betrkv-10"}]
        }
        """;

    [Fact]
    public void Complete_response_is_read_field_by_field_with_confidence()
    {
        var s = ExtractionResponseParser.Parse(Complete);

        Assert.Equal("Grünpflege Meier GmbH", s.SupplierName.Value);
        Assert.Equal(0.97, s.SupplierName.Confidence);
        Assert.Equal("R-2026-117", s.InvoiceNumber.Value);
        Assert.Equal(new DateOnly(2026, 4, 30), s.InvoiceDate.Value);
        Assert.Equal(new DateOnly(2026, 1, 1), s.ServiceFrom.Value);
        Assert.Equal(595.00m, s.Gross.Value);
        Assert.Equal(500m, s.Net.Value);
        Assert.Equal(95m, s.Vat.Value);
        Assert.Equal("betrkv-10", s.CostType.Value);
        Assert.Equal(Section35a.TradesmanService, s.Section35a.Value);
        Assert.Equal(357m, s.LaborCosts.Value);
        Assert.False(s.FuelQuantity.HasValue);
        Assert.Equal("Rasenpflege", Assert.Single(s.Lines).Description);
    }

    [Fact]
    public void Code_fences_and_text_around_the_json_are_tolerated()
    {
        var s = ExtractionResponseParser.Parse("Hier ist das Ergebnis:\n```json\n" + Complete + "\n```\nViel Erfolg!");

        Assert.Equal(595.00m, s.Gross.Value);
    }

    [Fact]
    public void Bare_values_without_confidence_get_a_middle_confidence_and_count_as_uncertain()
    {
        var s = ExtractionResponseParser.Parse("""{"supplier_name": "Stadtwerke", "gross": "119,00", "invoice_date": "15.03.2026"}""");

        Assert.Equal(119m, s.Gross.Value);
        Assert.Equal(new DateOnly(2026, 3, 15), s.InvoiceDate.Value);
        Assert.Equal(0.5, s.Gross.Confidence);
        Assert.True(s.Gross.IsUncertain);
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("€ 12,50", 12.50)]
    [InlineData("12,50 EUR", 12.50)]
    [InlineData("-12,5", -12.5)]
    [InlineData("−12,50", -12.5)]
    [InlineData("12,50-", -12.5)]
    [InlineData("1.234", 1234)]
    [InlineData("1.234.567,89", 1234567.89)]
    [InlineData("0,19", 0.19)]
    [InlineData("42", 42)]
    public void Amounts_in_german_and_english_notation_are_understood(string text, double expected) =>
        Assert.Equal((decimal)expected, ExtractionResponseParser.ParseAmount(text));

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    [InlineData("12-50-1")]
    [InlineData(null)]
    public void Garbage_amounts_are_null(string? text) => Assert.Null(ExtractionResponseParser.ParseAmount(text));

    [Theory]
    [InlineData("2026-04-30", 2026, 4, 30)]
    [InlineData("30.04.2026", 2026, 4, 30)]
    [InlineData("1.4.2026", 2026, 4, 1)]
    [InlineData("30.04.26", 2026, 4, 30)]
    [InlineData("2026-04-30T00:00:00", 2026, 4, 30)]
    public void Dates_are_understood(string text, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), ExtractionResponseParser.ParseDate(text));

    [Theory]
    [InlineData("30. April 2026")]
    [InlineData("31.02.2026")]
    [InlineData("")]
    public void Unreadable_dates_are_null(string text) => Assert.Null(ExtractionResponseParser.ParseDate(text));

    [Fact]
    public void Confidence_as_percent_or_word_is_converted()
    {
        var s = ExtractionResponseParser.Parse("""{"gross": {"value": 10, "confidence": 85}, "net": {"value": 8, "confidence": "high"}, "vat": {"value": 2, "confidence": "low"}, "supplier_name": {"value": "X", "confidence": "93%"}}""");

        Assert.Equal(0.85, s.Gross.Confidence);
        Assert.Equal(0.9, s.Net.Confidence);
        Assert.Equal(0.3, s.Vat.Confidence);
        Assert.Equal(0.93, s.SupplierName.Confidence, 2);
    }

    [Fact]
    public void Cost_type_given_as_name_is_mapped_to_the_key()
    {
        var s = ExtractionResponseParser.Parse("""{"gross": 10, "cost_type": {"value": "Gartenpflege", "confidence": 0.8}}""");

        Assert.Equal("betrkv-10", s.CostType.Value);
    }

    [Fact]
    public void Invalid_values_inside_valid_json_are_ignored_not_fatal()
    {
        var s = ExtractionResponseParser.Parse("""{"gross": {"value": "viel", "confidence": 1}, "invoice_date": {"value": "gestern", "confidence": 1}, "supplier_name": {"value": "  ", "confidence": 1}, "section_35a": {"value": "irgendwas", "confidence": 1}}""");

        Assert.False(s.Gross.HasValue);
        Assert.False(s.InvoiceDate.HasValue);
        Assert.False(s.SupplierName.HasValue);
        Assert.False(s.Section35a.HasValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ich kann diese Rechnung leider nicht lesen.")]
    [InlineData("{ das ist kein json }")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"error\": \"overloaded\"}")]
    [InlineData("{}")]
    [InlineData("{\"gross\": {\"value\": 5")]
    public void Malformed_model_output_raises_a_friendly_extraction_exception(string? output)
    {
        var ex = Assert.Throws<ExtractionException>(() => ExtractionResponseParser.Parse(output));

        Assert.DoesNotContain("Exception", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Antwort des Modells", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Very_long_texts_and_many_lines_are_limited()
    {
        var lines = string.Join(",", Enumerable.Range(0, 80).Select(i => $$"""{"description": "Zeile {{i}}", "amount": 1}"""));
        var json = $$"""{"supplier_name": "{{new string('x', 500)}}", "lines": [{{lines}}]}""";

        var s = ExtractionResponseParser.Parse(json);

        Assert.Equal(200, s.SupplierName.Value!.Length);
        Assert.Equal(50, s.Lines.Count);
    }

    [Fact]
    public void Parse_accepts_a_json_element_from_the_tool_call()
    {
        using var document = JsonDocument.Parse(Complete);

        Assert.Equal("R-2026-117", ExtractionResponseParser.Parse(document.RootElement).InvoiceNumber.Value);
    }
}
