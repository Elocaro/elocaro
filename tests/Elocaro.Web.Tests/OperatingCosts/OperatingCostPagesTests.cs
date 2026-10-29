using Elocaro.Application.Accounts;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.OperatingCosts;

/// <summary>Allocation keys, agreed cost types and the preview of the operating cost statement in the administration.</summary>
public class OperatingCostPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid house;
    private Guid apartment1;
    private Guid apartment2;
    private Guid leaseAnna;

    private async Task SeedAsync()
    {
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            apartment1 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            apartment2 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(apartment1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            await properties.SetUnitAreaAsync(apartment2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct);
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var leases = sp.GetRequiredService<ILeaseService>();

            // Wohnung 2 stays vacant: the landlord bears its share.
            leaseAnna = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = apartment1, Start = new DateOnly(2024, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)).Id!.Value;
            await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-1", InvoiceDate = new DateOnly(2025, 2, 1), Gross = 1000m },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2025, 1, 1), To = new DateOnly(2025, 12, 31) },
                Ct);
            return true;
        });
    }

    [Fact]
    public async Task Supply_contract_page_lists_contracts_with_tariffs_and_meters()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var empty = await admin.GetAsync($"/verwaltung/objekte/{house}/versorgung");
        Assert.Contains("Noch keine Versorgungsverträge", empty.Document.Body!.TextContent, StringComparison.Ordinal);

        var created = await App.WithScopeAsync(async sp =>
        {
            var meter = (await sp.GetRequiredService<Elocaro.Application.Metering.IMeterService>().CreateAsync(
                house,
                new Elocaro.Application.Metering.MeterCreation { Data = new Elocaro.Application.Metering.MeterInput { Type = Elocaro.Domain.Metering.MeterType.ColdWater, Name = "Hauptzähler", Number = "H-1" }, InstalledOn = new DateOnly(2024, 1, 1), InitialReading = 10m },
                Ct)).Id!.Value;
            return await sp.GetRequiredService<ISupplyContracts>().CreateAsync(
                house,
                new SupplyContractInput
                {
                    Name = "Wasser und Abwasser",
                    Supplier = "Stadtwerke",
                    CostType = "betrkv-02",
                    MeterType = Elocaro.Domain.Metering.MeterType.ColdWater,
                    MeterIds = [meter],
                    Tariff = new SupplyTariffInput { From = new DateOnly(2025, 1, 1), UnitPrice = 4.5m, BaseFeePerMonth = 10m },
                },
                Ct);
        });
        Assert.True(created.Succeeded, string.Join(", ", created.Errors));

        var page = await admin.GetAsync(empty.Path);
        var text = page.Document.Body!.TextContent;
        Assert.Contains("Wasser und Abwasser", text, StringComparison.Ordinal);
        Assert.Contains("Stadtwerke", text, StringComparison.Ordinal);
        Assert.Contains("H-1", text, StringComparison.Ordinal);
        Assert.Contains("4,5", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_shows_cost_types_tenant_statement_vacancy_and_the_calculation_protocol()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var keys = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");
        Assert.Contains("kein Schlüssel (Standard: Wohnfläche)", keys.Document.QuerySelector("#key-betrkv-01")!.TextContent, StringComparison.Ordinal);
        // Entering a key in the form is covered by the browser tests.
        var set = await App.WithScopeAsync(sp => sp.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct));
        Assert.True(set.Succeeded);
        keys = await admin.GetAsync(keys.Path);
        Assert.Contains("Wohnfläche", keys.Document.QuerySelector("#key-betrkv-01")!.TextContent, StringComparison.Ordinal);

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=2025");

        Assert.Contains("Vorschau", page.Document.QuerySelector("#preview-notice")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Buchhaltungsbeginn", page.Document.QuerySelector("#problems")!.TextContent, StringComparison.Ordinal);

        // 1,000.00 € by area 60 : 40 (vacant Wohnung 2) → Anna 600.00 €, landlord (vacancy) 400.00 €
        var row = page.Document.QuerySelector("#type-betrkv-01")!.TextContent;
        Assert.Contains("1.000,00 €", row, StringComparison.Ordinal);
        Assert.Contains("600,00 €", row, StringComparison.Ordinal);
        Assert.Contains("400,00 €", row, StringComparison.Ordinal);
        Assert.Contains("Leerstand Wohnung 2", page.Document.QuerySelector("#landlord")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.000,00 €", page.Document.QuerySelector("#cost-chart")!.TextContent, StringComparison.Ordinal);
        var statement = page.Document.QuerySelector($"#tenant-{leaseAnna}")!;
        Assert.Contains("Anna Schmidt", statement.TextContent, StringComparison.Ordinal);
        Assert.Contains("Summe Betriebskosten", statement.TextContent, StringComparison.Ordinal);
        Assert.Contains("Nachzahlung", statement.TextContent, StringComparison.Ordinal);
        var protocol = statement.QuerySelector("details.protocol")!.TextContent;
        Assert.Contains("Formel: Kosten × Σ(Wohnfläche × Tage) des Mieters", protocol, StringComparison.Ordinal);
        Assert.Contains("Werte: 1.000,00 € × 21.900 ÷ 36.500 m²·Tage", protocol, StringComparison.Ordinal);
        Assert.Contains("Stadt Köln, Rechnung G-1", protocol, StringComparison.Ordinal);

        Assert.Contains("zwischen 2000 und 2100", (await admin.GetAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=1800")).Text, StringComparison.Ordinal);
        Assert.Contains("Objekt nicht gefunden", (await admin.GetAsync($"/verwaltung/objekte/{Guid.NewGuid()}/nebenkosten")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keys_are_validated_keep_their_history_and_can_be_ended()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (missingMeter, fixedPercent, units, ended) = await App.WithScopeAsync(async sp =>
        {
            var allocation = sp.GetRequiredService<ICostAllocation>();
            return (
                await allocation.SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-02", From = new DateOnly(2020, 1, 1), Key = AllocationKey.Consumption }, Ct),
                await allocation.SetRuleAsync(house, new CostRuleInput
                {
                    CostType = "betrkv-14",
                    From = new DateOnly(2020, 1, 1),
                    Key = AllocationKey.FixedPercent,
                    Shares = [new UnitShareInput { UnitId = apartment1.ToString(), Percent = "40" }, new UnitShareInput { UnitId = apartment2.ToString(), Percent = "35" }],
                }, Ct),
                await allocation.SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-14", From = new DateOnly(2026, 1, 1), Key = AllocationKey.Units, PreDeductionPercent = 10m, PreDeductionNote = "Garten" }, Ct),
                await allocation.EndRuleAsync(house, "betrkv-14", new DateOnly(2026, 6, 30), Ct));
        });
        Assert.False(missingMeter.Succeeded);
        Assert.Contains("Zählertyp", string.Join(" ", missingMeter.Errors), StringComparison.Ordinal);
        Assert.True(fixedPercent.Succeeded);
        Assert.True(units.Succeeded);
        Assert.True(ended.Succeeded);

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");
        var row = page.Document.QuerySelector("#key-betrkv-14")!.TextContent;
        Assert.Contains("2 Stände", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_flat_mode_and_subtypes_are_shown_on_the_keys_page()
    {
        await SeedAsync();
        Guid flat = Guid.Empty;
        await App.WithScopeAsync<bool>(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            flat = (await properties.CreateUnitAsync(house, new UnitInput { Name = "WG" }, Ct)).Id!.Value;
            await properties.CreateRoomAsync(flat, new RoomInput { Name = "Zimmer 1" }, Ct);
            return true;
        });
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        await App.WithScopeAsync(async sp =>
        {
            Assert.True((await sp.GetRequiredService<ICostAllocation>().SetSharedFlatBillingAsync(flat, SharedFlatBilling.ByRoom, Ct)).Succeeded);
            Assert.True((await sp.GetRequiredService<ICostSubtypes>().AddAsync("betrkv-13", "Gebäudeversicherung", Ct)).Succeeded);
            return 0;
        });

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");
        Assert.Contains("WG", page.Document.QuerySelector("#shared-flats")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Gebäudeversicherung", page.Document.QuerySelector("#subtypes")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agreed_cost_types_of_a_lease_change_the_preview()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync($"/verwaltung/vertraege/{leaseAnna}/kostenarten");
        Assert.NotEmpty(page.Document.QuerySelectorAll("#agreement tbody tr"));

        // Unticking a cost type in the table is covered by the browser tests.
        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<ICostAllocation>().SetLeaseAgreementAsync(leaseAnna, ["betrkv-01"], Ct))).Succeeded);

        var preview = await admin.GetAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=2025");
        Assert.Contains("nicht vereinbart", preview.Document.QuerySelector($"#tenant-{leaseAnna}")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Im Vertrag nicht vereinbart", preview.Document.QuerySelector("#landlord")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Vereinbarte Kostenarten", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_require_the_finance_right()
    {
        await SeedAsync();
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.MasterData]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/objekte/{house}/nebenkosten", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync($"/verwaltung/vertraege/{leaseAnna}/kostenarten", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
