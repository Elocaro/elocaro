using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Portal;

public class PortalPagesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_sees_own_lease_and_gets_not_found_for_a_foreign_one()
    {
        var (tenant, own, foreign) = await TenantWithLeasesAsync();
        using var _ = tenant;

        var mine = await tenant.GetAsync($"/portal/vertrag/{own}", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, mine.Status);
        Assert.Contains("650,00 €", mine.TextWithStreaming, StringComparison.Ordinal);

        var other = await tenant.GetAsync($"/portal/vertrag/{foreign}", followRedirects: false);
        Assert.Equal(HttpStatusCode.NotFound, other.Status);
        Assert.DoesNotContain("700,00 €", other.TextWithStreaming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Single_lease_is_opened_directly_from_the_contract_tab()
    {
        var (tenant, own, _) = await TenantWithLeasesAsync();
        using var _ = tenant;

        Assert.Equal($"/portal/vertrag/{own}", (await tenant.GetAsync("/portal/vertrag")).Path);
    }

    [Fact]
    public async Task Released_document_is_listed_and_opening_it_marks_it_as_read()
    {
        var (tenant, own, _) = await TenantWithLeasesAsync();
        using var _ = tenant;
        var fileId = await ShareDocumentAsync(own, "Mietvertrag Anna");

        var before = await tenant.GetAsync("/portal/dokumente");
        Assert.Contains("Mietvertrag Anna", before.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("neu", before.TextWithStreaming, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync($"/dateien/{fileId}", followRedirects: false)).Status);

        var after = await tenant.GetAsync("/portal/dokumente");
        Assert.Contains("gelesen", after.TextWithStreaming, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contact_change_is_submitted_from_the_portal_and_confirmed_by_the_administrator()
    {
        var (tenant, _, _) = await TenantWithLeasesAsync();
        using var _ = tenant;

        var page = await tenant.GetAsync("/portal/kontakt");
        var result = await tenant.SubmitAsync(page, "contact-change", new Dictionary<string, string> { ["Input.Phone"] = "0221 5555" });
        Assert.Contains("Änderungsantrag", result.TextWithStreaming, StringComparison.Ordinal);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;
        var list = await admin.GetAsync("/verwaltung/personen/aenderungen");
        Assert.Equal(HttpStatusCode.OK, list.Status);
    }

    [Fact]
    public async Task Tenant_cannot_open_the_administration()
    {
        var (tenant, _, _) = await TenantWithLeasesAsync();
        using var _ = tenant;

        var response = await tenant.GetAsync("/verwaltung/personen/aenderungen", followRedirects: false);
        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Ticket_pages_are_reachable_for_the_tenant_but_the_administration_stays_closed()
    {
        var (tenant, _, _) = await TenantWithLeasesAsync();
        using var _ = tenant;

        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal/tickets", followRedirects: false)).Status);
        var form = await tenant.GetAsync("/portal/tickets/neu", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, form.Status);
        Assert.Contains("Notfall", form.TextWithStreaming, StringComparison.Ordinal);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/verwaltung/tickets", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/service/tickets/" + Guid.NewGuid(), followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Administration_and_service_ticket_lists_open()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/verwaltung/tickets", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/verwaltung/tickets/neu", followRedirects: false)).Status);

        var (caretaker, _) = await SignedInAsAsync(Roles.Caretaker);
        using var __ = caretaker;
        Assert.Equal(HttpStatusCode.OK, (await caretaker.GetAsync("/service", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Notice_and_message_pages_are_reachable_for_the_tenant_but_the_administration_stays_closed()
    {
        var (tenant, _, _) = await TenantWithLeasesAsync();
        using var _ = tenant;

        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal/aushaenge", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal/nachrichten", followRedirects: false)).Status);
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/portal/nachrichten/neu", followRedirects: false)).Status);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/verwaltung/aushaenge", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/verwaltung/nachrichten", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/verwaltung/aushaenge/" + Guid.NewGuid() + "/aushang.pdf", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Administration_notice_and_message_pages_open()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        foreach (var path in new[] { "/verwaltung/aushaenge", "/verwaltung/aushaenge/neu", "/verwaltung/nachrichten", "/verwaltung/nachrichten/neu" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path, followRedirects: false)).Status);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/verwaltung/aushaenge/" + Guid.NewGuid() + "/aushang.pdf", followRedirects: false)).Status);
    }

    [Fact]
    public async Task Contractor_link_page_is_public_but_shows_nothing_for_an_unknown_token()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        using var visitor = App.Browser();
        var page = await visitor.GetAsync("/auftrag/unbekannt", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/auftrag/unbekannt/datei/" + Guid.NewGuid(), followRedirects: false)).Status);
    }

    [Fact]
    public async Task Routine_pages_open_for_administration_and_caretaker_but_not_for_tenants()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/verwaltung/tickets/routinen", followRedirects: false)).Status);

        var (caretaker, _) = await SignedInAsAsync(Roles.Caretaker);
        using var __ = caretaker;
        Assert.Equal(HttpStatusCode.OK, (await caretaker.GetAsync("/service/routinen", followRedirects: false)).Status);

        var (tenant, _, _) = await TenantWithLeasesAsync();
        using var ___ = tenant;
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/service/routinen", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Administration_ticket_details_show_work_links_and_invoice_sections()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var id = await App.WithScopeAsync(async sp =>
        {
            var propertyId = (await sp.GetRequiredService<MietHahn.Application.Properties.IPropertyService>().CreateAsync(
                new MietHahn.Application.Properties.PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value;
            var db = sp.GetRequiredService<MietHahn.Infrastructure.Persistence.MietHahnDbContext>();
            var ticket = new MietHahn.Domain.Tickets.Ticket("Wasserhahn tropft", "Im Bad", MietHahn.Domain.Tickets.TicketCategory.Water, MietHahn.Domain.Tickets.TicketUrgency.Normal, propertyId, null, null, null, DateTimeOffset.UtcNow);
            db.Add(ticket);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return ticket.Id;
        });

        var page = await admin.GetAsync($"/verwaltung/tickets/{id}", followRedirects: false);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Wasserhahn tropft", page.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("Link für Handwerker ohne Zugang", page.TextWithStreaming, StringComparison.Ordinal);
    }

    private async Task<(TestBrowser Browser, Guid Own, Guid Foreign)> TenantWithLeasesAsync()
    {
        var (browser, userId) = await SignedInAsAsync(Roles.Tenant);
        var (own, foreign) = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit1 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var unit2 = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, Ct)).Id!.Value;
            var leases = sp.GetRequiredService<ILeaseService>();
            var start = new DateOnly(2026, 1, 1);
            var own = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit1, Start = start, TenantIds = [anna], BaseRent = 650m }, Ct)).Id!.Value;
            var foreign = (await leases.CreateAsync(new LeaseCreation { RentalUnitId = unit2, Start = start, TenantIds = [ben], BaseRent = 700m }, Ct)).Id!.Value;

            var db = sp.GetRequiredService<MietHahnDbContext>();
            (await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct)).UserId = userId;
            await db.SaveChangesAsync(Ct);
            return (own, foreign);
        });
        return (browser, own, foreign);
    }

    private Task<Guid> ShareDocumentAsync(Guid leaseId, string title) => App.WithScopeAsync(async sp =>
    {
        using var pdf = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
        var file = await sp.GetRequiredService<IFileStore>().SaveAsync(pdf, "vertrag.pdf", Ct);
        var result = await sp.GetRequiredService<IDocumentLibrary>().CreateAsync(file.Id, new DocumentInput { Title = title, LeaseId = leaseId, SharedWithTenants = true }, Ct);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return file.Id;
    });
}
