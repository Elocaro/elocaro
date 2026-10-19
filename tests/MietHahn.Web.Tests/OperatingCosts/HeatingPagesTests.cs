using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Heating;
using MietHahn.Domain.Metering;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.OperatingCosts;

/// <summary>Settings page for the heating costs and the heating part of the preview.</summary>
public class HeatingPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid house;
    private Guid leaseAnna;

    /// <summary>
    /// House with two apartments (60 m² rented to Anna, 40 m² vacant), billing year 2025. Gas bill 900.00 € = 800.00 € gas + 100.00 € CO₂ costs for
    /// 8,000 kWh and 1,600 kg CO₂; heat meters 3,000 kWh and 1,000 kWh, hot water 20 m³ and 10 m³; no heat meter in the hot water circuit (formula).
    /// </summary>
    private async Task SeedAsync()
    {
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var apartment1 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var apartment2 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(apartment1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            await properties.SetUnitAreaAsync(apartment2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct);
            var anna = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            leaseAnna = (await sp.GetRequiredService<ILeaseService>().CreateAsync(
                new LeaseCreation { RentalUnitId = apartment1, Start = new DateOnly(2024, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m, HeatingCostPrepayment = 40m }, Ct)).Id!.Value;

            var receipts = sp.GetRequiredService<IReceiptService>();
            var receipt = (await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Stadtwerke", InvoiceNumber = "G-1", InvoiceDate = new DateOnly(2026, 1, 15), Gross = 900m, FuelQuantity = 8000m, FuelUnit = "kWh", Co2Kg = 1600m },
                [],
                new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 800m, HeatingKind = HeatingCostKind.Fuel, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
                Ct)).Id!.Value;
            await receipts.AddItemAsync(receipt, new CostItemInput { CostType = "betrkv-06", PropertyId = house, Amount = 100m, HeatingKind = HeatingCostKind.CarbonPrice, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) }, Ct);

            var meters = sp.GetRequiredService<IMeterService>();
            foreach (var (unit, type, number, end) in new[]
            {
                (apartment1, MeterType.Heat, "W-H1", 3000m),
                (apartment2, MeterType.Heat, "W-H2", 1000m),
                (apartment1, MeterType.HotWater, "W-W1", 20m),
                (apartment2, MeterType.HotWater, "W-W2", 10m),
            })
            {
                var meter = (await meters.CreateAsync(
                    house,
                    new MeterCreation { Data = new MeterInput { Type = type, Name = type.ToString(), Number = number, AssignmentId = unit }, InstalledOn = new DateOnly(2024, 12, 31), InitialReading = 0m },
                    Ct)).Id!.Value;
                await meters.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(2025, 12, 31), Value = end, Confirmed = true }, null, Ct);
            }

            return true;
        });
    }

    private static IHtmlInputElement Input(Page page, string selector) => (IHtmlInputElement)page.Document.QuerySelector(selector)!;

    private static decimal Number(Page page, string selector) => decimal.Parse(Input(page, selector).Value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Settings_page_shows_defaults_validates_the_range_and_saves()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/heizung");
        Assert.Contains("Standardwerten", page.Document.QuerySelector("#defaults-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stand/prüfen", page.Document.QuerySelector("#legal-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(70m, Number(page, "#heating-percent"));
        Assert.True(Input(page, "#heating-joint").IsChecked);

        // 100 % consumption is not allowed (§ 7 para. 1 HeizkostenV)
        var invalid = await admin.SubmitAsync(page, "heating", new Dictionary<string, string> { ["Data.ConsumptionPercent"] = "100" });
        Assert.Contains("zwischen 50 und 70", invalid.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("wurden gespeichert", invalid.Text, StringComparison.Ordinal);

        Input(page, "#heating-joint").IsChecked = false;
        var saved = await admin.SubmitAsync(page, "heating", new Dictionary<string, string> { ["Data.ConsumptionPercent"] = "60", ["Data.HotWaterTemperature"] = "55" });
        Assert.Contains("Die Heizungseinstellungen wurden gespeichert.", saved.Text, StringComparison.Ordinal);
        Assert.Null(saved.Document.QuerySelector("#defaults-notice"));
        Assert.Equal(60m, Number(saved, "#heating-percent"));
        Assert.Equal(55m, Number(saved, "#heating-temperature"));
        Assert.False(Input(saved, "#heating-joint").IsChecked);

        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/heizung")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_shows_heating_split_carbon_level_vacancy_and_the_balance_with_prepayments()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=2025");

        // Total 900.00 €. Formula: 2.5 × 30 m³ × 50 K = 3,750 kWh of 8,000 kWh = 46.875 % → hot water 421.88 €, heating 478.12 €.
        // Anna's share (heat meter 75 %, area 60 %): heating 337.08 + hot water 272.81 = 609.89 €; CO₂: 16 kg/(m²·a) → level 2, landlord 10 % of her 1/9:
        // 609.89 × 100/900 × 10 % = 6.78 € → 603.11 €; Wohnung 2 is vacant and stays with the landlord.
        Assert.False(page.Document.QuerySelector("#problems")?.TextContent.Contains("nicht belastbar", StringComparison.Ordinal) ?? false);
        Assert.NotNull(page.Document.QuerySelector("#type-heating-heat"));
        Assert.NotNull(page.Document.QuerySelector("#type-heating-hotwater"));
        Assert.Contains("Stufe 2", page.Document.QuerySelector("#type-heating-co2")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stufe 2", page.Document.QuerySelector("#section-carbon")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stand/prüfen", page.Document.QuerySelector("#section-carbon")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Formel Q = 2,5 × V × (t − 10)", page.Document.QuerySelector("#section-heating")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stand/prüfen", page.Document.QuerySelector("#preview-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("CO₂-Kostenanteil des Vermieters", page.Document.QuerySelector("#landlord")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Leerstand Wohnung 2", page.Document.QuerySelector("#landlord")!.TextContent, StringComparison.Ordinal);

        var heating = page.Document.QuerySelector($"#heating-{leaseAnna}")!.TextContent;
        Assert.Contains("337,08 €", heating, StringComparison.Ordinal);
        Assert.Contains("272,81 €", heating, StringComparison.Ordinal);
        Assert.Contains("-6,78 €", heating, StringComparison.Ordinal);
        Assert.Contains("603,11 €", heating, StringComparison.Ordinal);
        Assert.Contains("Heizkostenvorauszahlungen", heating, StringComparison.Ordinal);
        Assert.Contains("CO₂-Kosten: Vermieteranteil", page.Document.QuerySelector($"#tenant-{leaseAnna}")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Heizkostenvorauszahlungen gezahlt (Verrechnung", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cost_item_form_offers_the_heating_kind_and_pages_require_the_finance_right()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var receiptId = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Wartung GmbH", InvoiceDate = new DateOnly(2025, 3, 1), Gross = 500m }, [], null, Ct)).Id!.Value);

        var receipt = await admin.GetAsync($"/verwaltung/belege/{receiptId}");
        var options = receipt.Document.QuerySelector("#item-heating-kind")!.TextContent;
        Assert.Contains("Brennstoff", options, StringComparison.Ordinal);
        Assert.Contains("CO₂-Kosten laut Lieferant", options, StringComparison.Ordinal);

        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _2 = manager;
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/objekte/{house}/heizung", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
