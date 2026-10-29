using Elocaro.Application.Accounts;
using Elocaro.Application.Heating;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Heating;
using Elocaro.Domain.Metering;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.OperatingCosts;

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

    [Fact]
    public async Task Settings_page_shows_defaults_and_the_range_is_validated_by_the_service()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/heizung");
        Assert.Contains("Standardwerten", page.Document.QuerySelector("#defaults-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stand/prüfen", page.Document.QuerySelector("#legal-notice")!.TextContent, StringComparison.Ordinal);

        // 100 % consumption is not allowed (§ 7 para. 1 HeizkostenV); the form itself is covered by the browser tests.
        var invalid = await App.WithScopeAsync(async sp =>
        {
            var settings = sp.GetRequiredService<IHeatingSettings>();
            var input = (await settings.GetAsync(house, Ct))!.Input;
            input.ConsumptionPercent = 100m;
            return await settings.SaveAsync(house, input, Ct);
        });
        var saved = await App.WithScopeAsync(async sp =>
        {
            var settings = sp.GetRequiredService<IHeatingSettings>();
            var input = (await settings.GetAsync(house, Ct))!.Input;
            input.ConsumptionPercent = 60m;
            input.JointPlant = false;
            return await settings.SaveAsync(house, input, Ct);
        });
        Assert.False(invalid.Succeeded);
        Assert.Contains("zwischen 50 und 70", string.Join(" ", invalid.Errors), StringComparison.Ordinal);
        Assert.True(saved.Succeeded);
        Assert.Null((await admin.GetAsync(page.Path)).Document.QuerySelector("#defaults-notice"));

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
    public async Task Heating_settings_require_the_finance_right()
    {
        await SeedAsync();
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/objekte/{house}/heizung", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
