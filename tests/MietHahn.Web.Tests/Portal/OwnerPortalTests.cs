using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Portal;

public class OwnerPortalTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Two houses: "Lindenweg 3" belongs to the signed-in owner with 40 %, "Buchenweg 1" to somebody else.</summary>
    private async Task<(TestBrowser Owner, Guid Own, Guid Foreign)> OwnerAsync()
    {
        var (browser, userId) = await SignedInAsAsync(Roles.Owner);
        var (own, foreign) = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var a = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var b = (await properties.CreateAsync(new PropertyInput { Name = "Buchenweg 1", Street = "Buchenweg 1", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var me = (await people.CreateAsync(new PersonInput { FirstName = "Olga", LastName = "Owner" }, null, Ct)).Id!.Value;
            var other = (await people.CreateAsync(new PersonInput { FirstName = "Paul", LastName = "Fremd" }, null, Ct)).Id!.Value;
            var db = sp.GetRequiredService<MietHahnDbContext>();
            (await db.FindAsync<MietHahn.Domain.People.Person>([me], Ct))!.UserId = userId;
            var owners = sp.GetRequiredService<IPropertyOwners>();
            await owners.AddAsync(a, new OwnerInput { PersonId = me, SharePercent = 40m, ValidFrom = new DateOnly(2020, 1, 1) }, Ct);
            await owners.AddAsync(a, new OwnerInput { PersonId = other, SharePercent = 60m, ValidFrom = new DateOnly(2020, 1, 1) }, Ct);
            await owners.AddAsync(b, new OwnerInput { PersonId = other, SharePercent = 100m }, Ct);
            foreach (var property in new[] { a, b })
            {
                var receipt = new Receipt(new DateOnly(2026, 2, 1), 300m) { SupplierName = "Stadt Köln", PaidOn = new DateOnly(2026, 3, 1) };
                receipt.AddCostItem("betrkv-01", 300m, property, Period.CalendarYear(2026));
                db.Add(receipt);
            }

            await db.SaveChangesAsync(Ct);
            return (a, b);
        });
        return (browser, own, foreign);
    }

    [Fact]
    public async Task Owner_sees_the_own_properties_and_only_the_own_share_of_the_figures()
    {
        var (owner, own, foreign) = await OwnerAsync();
        using var _ = owner;

        var start = await owner.GetAsync("/eigentuemer");
        Assert.Contains("Lindenweg 3", start.TextWithStreaming, StringComparison.Ordinal);
        Assert.Contains("40 %", start.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("Buchenweg", start.TextWithStreaming, StringComparison.Ordinal);

        var report = await owner.GetAsync($"/eigentuemer/objekte/{own}/anlage-v?jahr=2026");
        Assert.Contains("120,00", report.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("300,00", report.TextWithStreaming, StringComparison.Ordinal);

        using var csv = await owner.Http.GetAsync(new Uri($"/eigentuemer/objekte/{own}/anlage-v.csv?jahr=2026", UriKind.Relative), Ct);
        csv.EnsureSuccessStatusCode();
        Assert.Contains("40;Grundsteuer;120,00", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)), StringComparison.Ordinal);

        var strangerPage = await owner.GetAsync($"/eigentuemer/objekte/{foreign}/anlage-v?jahr=2026");
        Assert.Contains("Keine Auswertung", strangerPage.TextWithStreaming, StringComparison.Ordinal);
        Assert.DoesNotContain("300,00", strangerPage.TextWithStreaming, StringComparison.Ordinal);
        using var strangerCsv = await owner.Http.GetAsync(new Uri($"/eigentuemer/objekte/{foreign}/anlage-v.csv?jahr=2026", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, strangerCsv.StatusCode);
    }

    [Fact]
    public async Task Owner_cannot_open_the_administration_and_the_administration_exports_stay_closed()
    {
        var (owner, own, _) = await OwnerAsync();
        using var _ = owner;

        Assert.Equal("/konto/zugriff-verweigert", (await owner.GetAsync("/verwaltung/objekte", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await owner.GetAsync($"/verwaltung/objekte/{own}/anlage-v?jahr=2026", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.NotEqual(HttpStatusCode.OK, (await owner.GetAsync($"/verwaltung/objekte/{own}/anlage-v.csv?jahr=2026", followRedirects: false)).Status);
        Assert.Equal("/konto/zugriff-verweigert", (await owner.GetAsync("/portal", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    [Fact]
    public async Task Tenant_and_administration_accounts_do_not_get_the_owner_area()
    {
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/eigentuemer", followRedirects: false)).RedirectLocation!.AbsolutePath);

        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var __ = admin;
        Assert.Equal("/konto/zugriff-verweigert", (await admin.GetAsync("/eigentuemer", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
