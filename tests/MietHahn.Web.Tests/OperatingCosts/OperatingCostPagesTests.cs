using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Leases;
using MietHahn.Application.OperatingCosts;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.OperatingCosts;

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

    private static IHtmlInputElement Input(Page page, string selector) => (IHtmlInputElement)page.Document.QuerySelector(selector)!;

    [Fact]
    public async Task Preview_shows_cost_types_tenant_statement_vacancy_and_the_calculation_protocol()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var keys = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");
        Assert.Contains("kein Schlüssel (Standard: Wohnfläche)", keys.Document.QuerySelector("#key-betrkv-01")!.TextContent, StringComparison.Ordinal);
        keys = await admin.SubmitAsync(keys, "rule", new Dictionary<string, string>
        {
            ["Rule.CostType"] = "betrkv-01",
            ["Rule.From"] = "2020-01-01",
            ["Rule.Key"] = "LivingArea",
        });
        Assert.Contains("Der Umlageschlüssel wurde gespeichert.", keys.Text, StringComparison.Ordinal);
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
    public async Task Keys_form_validates_keeps_history_and_ends_keys()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");

        var missingMeter = await admin.SubmitAsync(page, "rule", new Dictionary<string, string> { ["Rule.CostType"] = "betrkv-02", ["Rule.From"] = "2020-01-01", ["Rule.Key"] = "Consumption" });
        Assert.Contains("Zählertyp", missingMeter.Text, StringComparison.Ordinal);

        page = await admin.SubmitAsync(page, "rule", new Dictionary<string, string> { ["Rule.CostType"] = "betrkv-14", ["Rule.From"] = "2020-01-01", ["Rule.Key"] = "FixedPercent", ["Rule.Shares[0].Percent"] = "40", ["Rule.Shares[1].Percent"] = "35" });
        Assert.Contains("Fester Prozentsatz: Wohnung 1 40 %, Wohnung 2 35 %", page.Document.QuerySelector("#key-betrkv-14")!.TextContent, StringComparison.Ordinal);

        page = await admin.SubmitAsync(page, "rule", new Dictionary<string, string> { ["Rule.CostType"] = "betrkv-14", ["Rule.From"] = "2026-01-01", ["Rule.Key"] = "Units", ["Rule.PreDeductionPercent"] = "10", ["Rule.PreDeductionNote"] = "Garten" });
        var row = page.Document.QuerySelector("#key-betrkv-14")!.TextContent;
        Assert.Contains("Einheiten", row, StringComparison.Ordinal);
        Assert.Contains("10 % (Garten)", row, StringComparison.Ordinal);
        Assert.Contains("2 Stände", row, StringComparison.Ordinal);

        page = await admin.SubmitAsync(page, "end-rule", new Dictionary<string, string> { ["End.CostType"] = "betrkv-14", ["End.LastDay"] = "2026-06-30" });
        Assert.Contains("Der Umlageschlüssel wurde beendet.", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_flat_mode_and_subtypes_are_maintained_on_the_keys_page()
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

        var page = await admin.GetAsync($"/verwaltung/objekte/{house}/umlageschluessel");
        var flatForm = page.Form("flats");
        Assert.True(flatForm.Elements.OfType<IHtmlInputElement>().Single(i => i.Type == "radio" && i.Value == "FlatRate").IsChecked);
        page = await admin.SubmitAsync(page, "flats", new Dictionary<string, string> { ["FlatForms[0].Mode"] = "ByRoom" });
        Assert.Contains("Der Abrechnungsmodus wurde gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.True(page.Form("flats").Elements.OfType<IHtmlInputElement>().Single(i => i.Type == "radio" && i.Value == "ByRoom").IsChecked);

        page = await admin.SubmitAsync(page, "subtype", new Dictionary<string, string> { ["Subtype.CostType"] = "betrkv-13", ["Subtype.Name"] = "Gebäudeversicherung" });
        Assert.Contains("Die Unterart wurde angelegt.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Gebäudeversicherung", page.Document.QuerySelector("#subtypes")!.TextContent, StringComparison.Ordinal);

        // The subtype is offered when adding a cost item
        var receiptId = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Versicherung AG", InvoiceDate = new DateOnly(2025, 3, 1), Gross = 500m }, [], null, Ct)).Id!.Value);
        var receipt = await admin.GetAsync($"/verwaltung/belege/{receiptId}");
        Assert.Contains("Gebäudeversicherung", receipt.Document.QuerySelector("#item-subtype")!.TextContent, StringComparison.Ordinal);

        var removeId = page.Document.QuerySelector("#subtypes button[name=RemoveId]")!.GetAttribute("value")!;
        page = await admin.SubmitAsync(page, "remove-subtype", new Dictionary<string, string> { ["RemoveId"] = removeId });
        Assert.Contains("Die Unterart wurde entfernt.", page.Text, StringComparison.Ordinal);
        Assert.Null(page.Document.QuerySelector("#subtypes table"));
    }

    [Fact]
    public async Task Agreed_cost_types_of_a_lease_are_saved_and_change_the_preview()
    {
        await SeedAsync();
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var page = await admin.GetAsync($"/verwaltung/vertraege/{leaseAnna}/kostenarten");
        Assert.All(page.Document.QuerySelectorAll("#agreement input[type=checkbox]").Cast<IHtmlInputElement>(), c => Assert.True(c.IsChecked));

        Input(page, "#agreed-betrkv-01").IsChecked = false;
        page = await admin.SubmitAsync(page, "agreement");
        Assert.Contains("Die vereinbarten Kostenarten wurden gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.False(Input(page, "#agreed-betrkv-01").IsChecked);
        Assert.True(Input(page, "#agreed-betrkv-02").IsChecked);

        var preview = await admin.GetAsync($"/verwaltung/objekte/{house}/nebenkosten?jahr=2025");
        Assert.Contains("nicht vereinbart", preview.Document.QuerySelector($"#tenant-{leaseAnna}")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Im Vertrag nicht vereinbart", preview.Document.QuerySelector("#landlord")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Vereinbarte Kostenarten", (await admin.GetAsync($"/verwaltung/vertraege/{leaseAnna}")).Text, StringComparison.Ordinal);
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
