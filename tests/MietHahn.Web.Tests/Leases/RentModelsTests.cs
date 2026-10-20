using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Leases;

public class RentModelsTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    [Fact]
    public async Task Graduated_rent_shows_planned_steps_and_rejects_a_step_within_a_year()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var id = await LeaseAsync(Today);
        var from = Today.AddYears(1);

        var tooEarly = await App.WithScopeAsync(async sp =>
        {
            var leases = sp.GetRequiredService<ILeaseService>();
            var ct = TestContext.Current.CancellationToken;
            Assert.True((await leases.SetRentModelAsync(id, new RentModelInput { Model = RentModel.Graduated }, ct)).Succeeded);
            Assert.True((await leases.AddRentStepAsync(id, new RentStepInput { From = from, BaseRent = 700m }, ct)).Succeeded);
            return await leases.AddRentStepAsync(id, new RentStepInput { From = from.AddMonths(6), BaseRent = 720m }, ct);
        });

        var lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("Mietmodell: Staffelmiete", lease.Text, StringComparison.Ordinal);
        Assert.Contains("geplant", lease.Text, StringComparison.Ordinal);
        Assert.False(tooEarly.Succeeded);
        Assert.Contains("mindestens ein Jahr unverändert", string.Join(" ", tooEarly.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Indexed_rent_suggests_the_new_rent_from_imported_Cpi_and_applies_it()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var start = Today.AddMonths(-14);
        var id = await LeaseAsync(start);
        var baseMonth = new DateOnly(start.Year, start.Month, 1);
        var newMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);

        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var leases = sp.GetRequiredService<ILeaseService>();
            Assert.True((await leases.SetRentModelAsync(id, new RentModelInput { Model = RentModel.Index, IndexBaseMonth = baseMonth, IndexBaseValue = 117.4m }, ct)).Succeeded);
            return 0;
        });
        var lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("Mietmodell: Indexmiete", lease.Text, StringComparison.Ordinal);
        Assert.Contains("Kein VPI-Wert neuer als der Ausgangsindex.", lease.Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var import = await sp.GetRequiredService<ICpiService>().ImportAsync($"Jahr;Monat;Index\n{baseMonth:yyyy-MM};117,4\n{newMonth:yyyy-MM};121,7\n", ct);
            Assert.Equal(2, import.Added);
            return 0;
        });
        lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("neue Kaltmiete 673,81 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains("+3,7 %", lease.Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().AdjustIndexAsync(id, new IndexAdjustmentInput { DeclaredOn = Today }, TestContext.Current.CancellationToken)).Succeeded);
        lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("673,81 €", lease.Text, StringComparison.Ordinal); // in the history from the month after next
        Assert.Contains("121,7", lease.Text, StringComparison.Ordinal);
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
