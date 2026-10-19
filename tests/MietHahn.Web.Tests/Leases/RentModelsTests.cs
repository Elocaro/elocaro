using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Leases;

public class RentModelsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    [Fact]
    public async Task Enter_graduated_rent_and_remove_rent_step()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var lease = await admin.GetAsync($"/verwaltung/vertraege/{await LeaseAsync(Today)}");

        lease = await admin.SubmitAsync(lease, "mietmodell", new Dictionary<string, string> { ["Input.Model"] = "Graduated" });
        Assert.Contains("Mietmodell: Staffelmiete", lease.Text, StringComparison.Ordinal);

        var from = Today.AddYears(1);
        lease = await admin.SubmitAsync(lease, "staffel", new Dictionary<string, string> { ["Graduated.From"] = Iso(from), ["Graduated.BaseRent"] = "700" });
        Assert.Contains("Die Staffel wurde gespeichert.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("geplant", lease.Text, StringComparison.Ordinal);

        var tooEarly = await admin.SubmitAsync(lease, "staffel", new Dictionary<string, string> { ["Graduated.From"] = Iso(from.AddMonths(6)), ["Graduated.BaseRent"] = "720" });
        Assert.Contains("mindestens ein Jahr unverändert", tooEarly.Text, StringComparison.Ordinal);

        var rentStepId = lease.Document.QuerySelector("button[name=RemoveStepId]")!.GetAttribute("value")!;
        lease = await admin.SubmitAsync(lease, "staffel-entfernen", new Dictionary<string, string> { ["RemoveStepId"] = rentStepId });
        Assert.Contains("Die Staffel wurde entfernt.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Noch keine Staffeln erfasst.", lease.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjust_indexed_rent_with_imported_Cpi()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var start = Today.AddMonths(-14);
        var leasePage = $"/verwaltung/vertraege/{await LeaseAsync(start)}";
        var baseMonth = new DateOnly(start.Year, start.Month, 1);

        var lease = await admin.SubmitAsync(await admin.GetAsync(leasePage), "mietmodell", new Dictionary<string, string>
        {
            ["Input.Model"] = "Index",
            ["Input.BaseMonth"] = baseMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ["Input.BaseValue"] = "117.4",
        });
        Assert.Contains("Mietmodell: Indexmiete", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Kein VPI-Wert neuer als der Ausgangsindex.", lease.Text, StringComparison.Ordinal);

        var newMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        var vpi = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/vpi"), "vpi-import", new Dictionary<string, string>
        {
            ["Import.Csv"] = $"Jahr;Monat;Index\n{baseMonth:yyyy-MM};117,4\n{newMonth:yyyy-MM};121,7\n",
        });
        Assert.Contains("Import: 2 neu, 0 geändert, 0 unverändert.", vpi.Text, StringComparison.Ordinal);

        lease = await admin.GetAsync(leasePage);
        Assert.Contains("neue Kaltmiete 673,81 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains("+3,7 %", lease.Text, StringComparison.Ordinal);

        lease = await admin.SubmitAsync(lease, "index-anpassung", new Dictionary<string, string> { ["Adjustment.DeclaredOn"] = Iso(Today) });
        Assert.Contains("Die Indexanpassung wurde übernommen.", lease.Text, StringComparison.Ordinal);
        Assert.Contains("673,81 €", lease.Text, StringComparison.Ordinal); // in the history from the month after next
        Assert.Contains("121,7", lease.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enter_Cpi_value_manually()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        var page = await admin.SubmitAsync(await admin.GetAsync("/verwaltung/vpi"), "vpi", new Dictionary<string, string> { ["Input.Month"] = "2026-09", ["Input.Value"] = "122.1" });

        Assert.Contains("Der Indexwert wurde gespeichert.", page.Text, StringComparison.Ordinal);
        Assert.Contains("September 2026", page.Text, StringComparison.Ordinal);
        Assert.Contains("122,1", page.Text, StringComparison.Ordinal);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Guid> LeaseAsync(DateOnly start) =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            return (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = start, TenantIds = [tenant], BaseRent = 650m }, ct)).Id!.Value;
        });
}
