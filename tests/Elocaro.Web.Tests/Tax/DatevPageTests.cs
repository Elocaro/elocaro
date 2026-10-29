using System.Net;
using System.Text;
using Elocaro.Application.Accounts;
using Elocaro.Application.Properties;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Tax;

public class DatevPageTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Page_opens_for_finance_and_the_export_can_be_downloaded_from_the_log()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var exportId = await App.WithScopeAsync(async sp =>
        {
            var propertyId = (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var db = sp.GetRequiredService<ElocaroDbContext>();
            var receipt = new Receipt(new DateOnly(2026, 2, 1), 300m) { SupplierName = "Stadt Köln", PaidOn = new DateOnly(2026, 3, 1), InvoiceNumber = "R-17" };
            receipt.AddCostItem("betrkv-01", 300m, propertyId, Period.CalendarYear(2026));
            db.Add(receipt);
            await db.SaveChangesAsync(Ct);
            var datev = sp.GetRequiredService<IDatevExport>();
            await datev.SaveSettingsAsync(new DatevSettingsInput { ConsultantNumber = 1001, ClientNumber = 4711 }, Ct);
            return (await datev.ExportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, false, "Admin", Ct)).Id!.Value;
        });

        var page = await admin.GetAsync("/verwaltung/finanzen/datev", followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("DATEV-Export", page.TextWithStreaming, StringComparison.Ordinal);

        using var file = await admin.Http.GetAsync(new Uri($"/verwaltung/finanzen/datev/{exportId}.csv", UriKind.Relative), Ct);
        file.EnsureSuccessStatusCode();
        var content = Encoding.GetEncoding(1252).GetString(await file.Content.ReadAsByteArrayAsync(Ct));
        Assert.StartsWith("\"EXTF\";700;21;", content, StringComparison.Ordinal);
        Assert.Contains("R-17", content, StringComparison.Ordinal);

        using var list = await admin.Http.GetAsync(new Uri("/verwaltung/finanzen/datev/buchungen.csv?von=2026-01-01&bis=2026-12-31", UriKind.Relative), Ct);
        list.EnsureSuccessStatusCode();
        using var missing = await admin.Http.GetAsync(new Uri($"/verwaltung/finanzen/datev/{Guid.NewGuid()}.csv", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_open_the_page_or_the_files()
    {
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync("/verwaltung/finanzen/datev", followRedirects: false)).RedirectLocation!.AbsolutePath);
        Assert.Equal("/konto/zugriff-verweigert", (await tenant.GetAsync($"/verwaltung/finanzen/datev/{Guid.NewGuid()}.csv", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }
}
