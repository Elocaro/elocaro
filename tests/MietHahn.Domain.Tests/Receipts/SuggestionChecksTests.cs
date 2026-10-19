using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.Tests.Receipts;

public class SuggestionChecksTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static Suggested<T> Sure<T>(T value) => new(value, 0.95);

    private static ReceiptSuggestion Valid() => new()
    {
        SupplierName = Sure("Grünpflege Meier"),
        InvoiceDate = Sure<DateOnly?>(new DateOnly(2026, 9, 30)),
        Gross = Sure<decimal?>(595m),
        Net = Sure<decimal?>(500m),
        Vat = Sure<decimal?>(95m),
    };

    [Fact]
    public void Consistent_suggestion_stays_unchanged_without_warnings()
    {
        var result = SuggestionChecks.Check(Valid(), Today);

        Assert.Empty(result.Warnings);
        Assert.Equal(595m, result.Gross.Value);
        Assert.Equal(0.95, result.Gross.Confidence);
        Assert.Empty(result.UncertainFields());
    }

    [Fact]
    public void Net_plus_vat_not_equal_to_gross_warns_and_marks_all_three_as_uncertain()
    {
        var result = SuggestionChecks.Check(Valid() with { Vat = Sure<decimal?>(90m) }, Today);

        Assert.Contains(result.Warnings, w => w.Contains("ergibt nicht den Bruttobetrag", StringComparison.Ordinal));
        Assert.Equal(["Gross", "Net", "Vat"], result.UncertainFields().Order(StringComparer.Ordinal));
        Assert.Equal(595m, result.Gross.Value); // not corrected silently, the user decides
    }

    [Fact]
    public void Missing_vat_is_derived_from_gross_and_net_with_lower_confidence()
    {
        var result = SuggestionChecks.Check(Valid() with { Vat = default }, Today);

        Assert.Equal(95m, result.Vat.Value);
        Assert.True(result.Vat.Confidence < 0.95);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Missing_gross_is_derived_from_net_and_vat()
    {
        var result = SuggestionChecks.Check(Valid() with { Gross = default }, Today);

        Assert.Equal(595m, result.Gross.Value);
    }

    [Fact]
    public void Unusual_vat_rate_warns()
    {
        var result = SuggestionChecks.Check(Valid() with { Gross = Sure<decimal?>(580m), Vat = Sure<decimal?>(80m) }, Today);

        Assert.Contains(result.Warnings, w => w.Contains("Umsatzsteuersatz", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(7.0, 0.49)]
    [InlineData(19.0, 3.61)]
    [InlineData(0.0, 0.0)]
    public void Common_vat_rates_are_accepted(double net, double vat)
    {
        var n = (decimal)net;
        var v = (decimal)vat;
        var result = SuggestionChecks.Check(Valid() with { Net = Sure<decimal?>(n), Vat = Sure<decimal?>(v), Gross = Sure<decimal?>(n + v) }, Today);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("Umsatzsteuersatz", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2_000_000)]
    [InlineData(-1_000_000.01)]
    [InlineData(0)]
    public void Implausible_gross_is_discarded(double amount)
    {
        var result = SuggestionChecks.Check(new ReceiptSuggestion { Gross = Sure<decimal?>((decimal)amount) }, Today);

        Assert.False(result.Gross.HasValue);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Credit_note_with_negative_amount_is_plausible()
    {
        var result = SuggestionChecks.Check(new ReceiptSuggestion { Gross = Sure<decimal?>(-119m), Net = Sure<decimal?>(-100m), Vat = Sure<decimal?>(-19m) }, Today);

        Assert.Equal(-119m, result.Gross.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Amount_with_more_than_two_decimals_is_rounded_and_marked_uncertain()
    {
        var result = SuggestionChecks.Check(new ReceiptSuggestion { Gross = Sure<decimal?>(10.456m) }, Today);

        Assert.Equal(10.46m, result.Gross.Value);
        Assert.True(result.Gross.IsUncertain);
    }

    [Fact]
    public void Date_before_2000_is_discarded_and_far_future_is_uncertain()
    {
        var old = SuggestionChecks.Check(new ReceiptSuggestion { InvoiceDate = Sure<DateOnly?>(new DateOnly(1970, 1, 1)) }, Today);
        var future = SuggestionChecks.Check(new ReceiptSuggestion { InvoiceDate = Sure<DateOnly?>(new DateOnly(2031, 5, 5)) }, Today);

        Assert.False(old.InvoiceDate.HasValue);
        Assert.True(future.InvoiceDate.HasValue);
        Assert.True(future.InvoiceDate.IsUncertain);
        Assert.NotEmpty(future.Warnings);
    }

    [Fact]
    public void Service_period_ending_before_its_start_is_discarded()
    {
        var result = SuggestionChecks.Check(Valid() with
        {
            ServiceFrom = Sure<DateOnly?>(new DateOnly(2026, 6, 30)),
            ServiceTo = Sure<DateOnly?>(new DateOnly(2026, 1, 1)),
        }, Today);

        Assert.False(result.ServiceFrom.HasValue);
        Assert.False(result.ServiceTo.HasValue);
        Assert.Contains(result.Warnings, w => w.Contains("Leistungszeitraum", StringComparison.Ordinal));
    }

    [Fact]
    public void Service_end_without_start_is_discarded()
    {
        var result = SuggestionChecks.Check(Valid() with { ServiceTo = Sure<DateOnly?>(new DateOnly(2026, 6, 30)) }, Today);

        Assert.False(result.ServiceTo.HasValue);
    }

    [Fact]
    public void Due_date_before_invoice_date_is_marked()
    {
        var result = SuggestionChecks.Check(Valid() with { DueOn = Sure<DateOnly?>(new DateOnly(2026, 8, 1)) }, Today);

        Assert.True(result.DueOn.IsUncertain);
        Assert.Contains(result.Warnings, w => w.Contains("Fälligkeitsdatum", StringComparison.Ordinal));
    }

    [Fact]
    public void Iban_with_wrong_checksum_is_discarded_and_valid_one_is_formatted()
    {
        var wrong = SuggestionChecks.Check(Valid() with { Iban = Sure("DE89370400440532013001") }, Today);
        var good = SuggestionChecks.Check(Valid() with { Iban = Sure("de89370400440532013000") }, Today);

        Assert.False(wrong.Iban.HasValue);
        Assert.Equal("DE89 3704 0044 0532 0130 00", good.Iban.Value);
    }

    [Fact]
    public void Unknown_cost_type_is_discarded()
    {
        var result = SuggestionChecks.Check(Valid() with { CostType = Sure("erfunden") }, Today);

        Assert.False(result.CostType.HasValue);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Known_cost_type_is_kept()
    {
        Assert.Equal("betrkv-14", SuggestionChecks.Check(Valid() with { CostType = Sure("betrkv-14") }, Today).CostType.Value);
    }

    [Fact]
    public void Labor_costs_above_gross_are_discarded_and_missing_labor_costs_doubt_section_35a()
    {
        var tooMuch = SuggestionChecks.Check(Valid() with { LaborCosts = Sure<decimal?>(700m), Section35a = Sure<Section35a?>(Section35a.TradesmanService) }, Today);
        var missing = SuggestionChecks.Check(Valid() with { Section35a = Sure<Section35a?>(Section35a.HouseholdService) }, Today);

        Assert.False(tooMuch.LaborCosts.HasValue);
        Assert.True(missing.Section35a.IsUncertain);
    }

    [Fact]
    public void Lines_not_matching_gross_or_net_warn_and_unknown_line_cost_types_are_dropped()
    {
        var matching = SuggestionChecks.Check(Valid() with { Lines = [new SuggestedLine("Arbeit", 300m, "betrkv-14"), new SuggestedLine("Material", 200m, null)] }, Today);
        var wrong = SuggestionChecks.Check(Valid() with { Lines = [new SuggestedLine("Arbeit", 300m, "gibtsnicht")] }, Today);

        Assert.Empty(matching.Warnings);
        Assert.Contains(wrong.Warnings, w => w.Contains("Positionen", StringComparison.Ordinal));
        Assert.Null(wrong.Lines[0].CostType);
    }

    [Fact]
    public void Negative_fuel_quantity_and_co2_are_discarded()
    {
        var result = SuggestionChecks.Check(Valid() with { FuelQuantity = Sure<decimal?>(-5m), Co2Kg = Sure<decimal?>(-1m) }, Today);

        Assert.False(result.FuelQuantity.HasValue);
        Assert.False(result.Co2Kg.HasValue);
    }

    [Fact]
    public void Confidence_below_threshold_marks_the_field_as_uncertain_only_when_present()
    {
        var suggestion = Valid() with { SupplierName = new Suggested<string>("Meier", 0.5), InvoiceNumber = new Suggested<string>(null, 0.1) };

        var uncertain = SuggestionChecks.Check(suggestion, Today).UncertainFields();

        Assert.Contains("SupplierName", uncertain);
        Assert.DoesNotContain("InvoiceNumber", uncertain);
    }
}
