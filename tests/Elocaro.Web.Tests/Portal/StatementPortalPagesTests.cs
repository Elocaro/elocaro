using System.Net;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Files;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Portal;

/// <summary>Sent statements in the portal: explanation page, receipt download, question prefill and the separation between tenants.</summary>
public class StatementPortalPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private const string Marker = "STEUERBESCHEID-INHALT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_reads_the_derivation_of_the_own_statement_and_never_sees_the_neighbour()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var list = await setup.Tenant.GetAsync("/portal/abrechnungen");
        Assert.Contains("2026", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnung 2", list.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains($"/portal/abrechnungen/{setup.OwnDispatch}", Hrefs(list));
        Assert.DoesNotContain($"/portal/abrechnungen/{setup.NeighbourDispatch}", Hrefs(list));

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var text = page.TextWithStreaming;
        Assert.Contains("1.000,00 € nach Wohnfläche: Ihr Anteil 600,00 €", text, StringComparison.Ordinal);
        Assert.Contains("Vergleich mit dem Vorjahr", text, StringComparison.Ordinal);
        Assert.Contains("Für das Vorjahr liegt keine an Sie versendete Abrechnung vor.", text, StringComparison.Ordinal);
        Assert.Contains("ersetzt keinen förmlichen Einwand", text, StringComparison.Ordinal);
        Assert.Contains($"/portal/nachrichten/neu?abrechnung={setup.OwnDispatch}&position=betrkv-01", Hrefs(page));
        Assert.DoesNotContain("Weber", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnung 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("400,00 €", text, StringComparison.Ordinal); // the neighbour's share

        var foreign = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.NeighbourDispatch}", followRedirects: false);
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.DoesNotContain("400,00 €", foreign.TextWithStreaming, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{Guid.NewGuid()}", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Receipt_download_works_only_for_released_receipts_of_the_own_dispatch()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        var own = $"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}";

        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync(own, followRedirects: false)).Status); // nothing released yet
        var before = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");
        Assert.Contains("noch keine Belege freigegeben", before.TextWithStreaming, StringComparison.Ordinal);

        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [setup.Receipt], Ct));

        var page = await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}");
        Assert.Contains("Stadt Köln", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains(own, Hrefs(page));
        var file = await setup.Tenant.GetAsync(own, followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, file.Status);
        Assert.Contains(Marker, file.Html, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.NeighbourDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync($"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.LetterFile}", followRedirects: false)).Status);

        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [], Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await setup.Tenant.GetAsync(own, followRedirects: false)).Status); // withdrawn
    }

    [Fact]
    public async Task Receipt_download_is_closed_for_visitors_and_for_the_administration()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        await App.WithScopeAsync(sp => sp.GetRequiredService<IStatementReceiptShares>().SetSharedAsync(setup.Statement, [setup.Receipt], Ct));
        var path = $"/portal/abrechnungen/{setup.OwnDispatch}/belege/{setup.Receipt}/{setup.ReceiptFile}";

        using var visitor = App.Browser();
        var anonymous = await visitor.GetAsync(path, followRedirects: false);
        Assert.Equal("/konto/anmelden", anonymous.RedirectLocation!.AbsolutePath);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;
        Assert.Equal("/konto/zugriff-verweigert", (await admin.GetAsync(path, followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await admin.GetAsync("/portal/abrechnungen", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task A_question_about_a_position_prefills_the_subject_and_warns_about_the_deadline()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var page = await setup.Tenant.GetAsync($"/portal/nachrichten/neu?abrechnung={setup.OwnDispatch}&position=betrkv-01");

        Assert.Contains("Frage zur Abrechnung 2026: Laufende öffentliche Lasten (Grundsteuer)", Values(page));
        Assert.Contains("ersetzt keinen förmlichen Einwand", page.Text, StringComparison.Ordinal);

        // Foreign dispatch: no prefill, no hint (the subject is built from the tenant's own statement only).
        var foreign = await setup.Tenant.GetAsync($"/portal/nachrichten/neu?abrechnung={setup.NeighbourDispatch}&position=betrkv-01");
        Assert.DoesNotContain(Values(foreign), v => v?.StartsWith("Frage zur Abrechnung", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("förmlichen Einwand", foreign.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_page_and_documents_point_to_the_statements()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;

        var start = await setup.Tenant.GetAsync("/portal");
        Assert.Contains($"/portal/abrechnungen/{setup.OwnDispatch}", Hrefs(start));
        var documents = await setup.Tenant.GetAsync("/portal/dokumente");
        Assert.Contains("/portal/abrechnungen", Hrefs(documents));
        Assert.Contains("/portal/abrechnungen", Hrefs(start));
    }

    [Fact]
    public async Task Administration_chooses_receipts_and_warns_about_data_of_third_parties()
    {
        var setup = await SetUpAsync();
        using var _ = setup.Tenant;
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;

        var page = await admin.GetAsync($"/verwaltung/abrechnungen/{setup.Statement}");

        Assert.Contains("Belege für Mieter freigeben", page.Text, StringComparison.Ordinal);
        Assert.Contains("Belege können Daten Dritter enthalten", page.Text, StringComparison.Ordinal);
        Assert.Contains("Stadt Köln", page.Document.QuerySelector("#receipt-share-table")?.TextContent, StringComparison.Ordinal);
    }

    /// <summary>All link targets of the page, including the parts delivered later by stream rendering.</summary>
    private static List<string?> Hrefs(Page page) =>
        [.. page.Document.QuerySelectorAll("a")
            .Concat(page.Document.QuerySelectorAll("blazor-ssr template").OfType<AngleSharp.Html.Dom.IHtmlTemplateElement>().SelectMany(t => t.Content.QuerySelectorAll("a")))
            .Select(a => a.GetAttribute("href"))];

    private static List<string?> Values(Page page) => [.. page.Document.QuerySelectorAll("input").Select(i => i.GetAttribute("value"))];

    private async Task<Setup> SetUpAsync()
    {
        var (tenant, userId) = await SignedInAsAsync(Roles.Tenant);
        var ids = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit1 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var unit2 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(unit1, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
            await properties.SetUnitAreaAsync(unit2, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct);
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct)).Id!.Value;
            var leases = sp.GetRequiredService<ILeaseService>();
            var leaseAnna = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit1, Start = new DateOnly(2025, 1, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)).Id!.Value;
            var leaseBen = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit2, Start = new DateOnly(2025, 1, 1), TenantIds = [ben], BaseRent = 400m, OperatingCostPrepayment = 80m }, Ct)).Id!.Value;
            Assert.True((await sp.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = "betrkv-01", From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct)).Succeeded);

            var db = sp.GetRequiredService<ElocaroDbContext>();
            (await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct)).UserId = userId;
            await db.SaveChangesAsync(Ct);

            var files = sp.GetRequiredService<IFileStore>();
            using var invoice = new MemoryStream(System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {Marker}\n%%EOF\n"));
            var invoiceFile = await files.SaveAsync(invoice, "grundsteuer.pdf", Ct);
            var receipt = await sp.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "GS-2026", InvoiceDate = new DateOnly(2026, 12, 15), Gross = 1000m },
                [invoiceFile.Id],
                new CostItemInput { CostType = "betrkv-01", PropertyId = house, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31) },
                Ct);
            Assert.True(receipt.Succeeded, string.Join(", ", receipt.Errors));
            var statement = await sp.GetRequiredService<IStatements>().ReleaseAsync(house, Period.CalendarYear(2026), true, null, Ct);
            Assert.True(statement.Succeeded, string.Join(", ", statement.Errors));

            using var letter = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
            var letterFile = await files.SaveAsync(letter, "abrechnung.pdf", Ct);
            var own = new StatementDispatch(statement.Id!.Value, leaseAnna, DispatchChannel.Post, new DateOnly(2027, 2, 1), letterFile.Id, null, null, 600m, 0m, null, null);
            var neighbour = new StatementDispatch(statement.Id.Value, leaseBen, DispatchChannel.Post, new DateOnly(2027, 2, 1), letterFile.Id, null, null, 400m, 0m, null, null);
            db.AddRange(own, neighbour);
            await db.SaveChangesAsync(Ct);
            return (Statement: statement.Id.Value, Own: own.Id, Neighbour: neighbour.Id, Receipt: receipt.Id!.Value, ReceiptFile: invoiceFile.Id, LetterFile: letterFile.Id);
        });
        return new Setup(tenant, ids.Statement, ids.Own, ids.Neighbour, ids.Receipt, ids.ReceiptFile, ids.LetterFile);
    }

    private sealed record Setup(TestBrowser Tenant, Guid Statement, Guid OwnDispatch, Guid NeighbourDispatch, Guid Receipt, Guid ReceiptFile, Guid LetterFile);
}
