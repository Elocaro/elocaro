using System.Globalization;
using Elocaro.Application.Accounts;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Leases;

public class LeasesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private DateOnly Today => GermanTime.Today(App.Clock.GetUtcNow());

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Data goes through the services; operating the interactive pages is covered by LeasesBrowserTests.
    private async Task<Guid> CreateLeaseAsync(Guid apartment, DateOnly start, Guid tenant, decimal baseRent = 650m, Action<LeaseCreation>? more = null) =>
        await App.WithScopeAsync(async sp =>
        {
            var creation = new LeaseCreation { RentalUnitId = apartment, Start = start, TenantIds = [tenant], BaseRent = baseRent };
            more?.Invoke(creation);
            var result = await sp.GetRequiredService<ILeaseService>().CreateAsync(creation, TestContext.Current.CancellationToken);
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            return result.Id!.Value;
        });

    [Fact]
    public async Task Room_lease_page_offers_the_other_rooms_of_the_shared_flat()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, _, anna, _) = await MasterDataAsync();
        var (room1, room2) = await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var ct = TestContext.Current.CancellationToken;
            var wg = (await properties.CreateUnitAsync(property, new UnitInput { Name = "WG" }, ct)).Id!.Value;
            return ((await properties.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 1" }, ct)).Id!.Value, (await properties.CreateRoomAsync(wg, new RoomInput { Name = "Zimmer 2" }, ct)).Id!.Value);
        });
        var id = await CreateLeaseAsync(room1, new DateOnly(Today.Year, Today.Month, 1), anna, 350m);

        var lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");

        Assert.Contains("Weiteres Zimmer", lease.Text, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, room2);
    }

    [Fact]
    public async Task Lease_with_two_tenants_rent_increase_and_termination_are_shown()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (property, apartment, anna, ben) = await MasterDataAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var id = await CreateLeaseAsync(apartment, start, anna, 650m, c =>
        {
            c.TenantIds = [anna, ben];
            c.OperatingCostPrepayment = 180m;
            c.HeatingCostPrepayment = 90.50m;
            c.People = 2;
            c.Data = new LeaseInput { Deposit = 1950m };
        });

        var lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("Anna Schmidt, Ben Weber", lease.Text, StringComparison.Ordinal);
        Assert.Contains("920,50 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains("1.950,00 €", lease.Text, StringComparison.Ordinal);

        // Occupancy in the property overview
        Assert.Contains("Anna Schmidt, Ben Weber", (await admin.GetAsync($"/verwaltung/objekte/{property}")).Text, StringComparison.Ordinal);

        // Rent increase from the effective date: old amount stays in the history
        var increase = start.AddMonths(2);
        var incoming = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(14); // after the 3rd working day
        await App.WithScopeAsync(async sp =>
        {
            var leases = sp.GetRequiredService<ILeaseService>();
            var ct = TestContext.Current.CancellationToken;
            Assert.True((await leases.SetComponentAsync(id, new ComponentInput { Kind = RentComponentType.BaseRent, From = increase, Amount = 700m }, ct)).Succeeded);
            Assert.True((await leases.TerminateAsync(id, new TerminationInput { ReceivedOn = incoming, By = TerminatingParty.Tenant }, ct)).Succeeded);
            return 0;
        });
        lease = await admin.GetAsync($"/verwaltung/vertraege/{id}");
        Assert.Contains("Verlauf", lease.Text, StringComparison.Ordinal);
        Assert.Contains("700,00 €", lease.Text, StringComparison.Ordinal);
        Assert.Contains($"{increase.AddDays(-1):dd.MM.yyyy}", lease.Text, StringComparison.Ordinal);
        var expected = Elocaro.Domain.Leases.Lease.TerminationDateForTenant(incoming);
        Assert.Contains($"endet am {expected:dd.MM.yyyy}", lease.Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().RevokeTerminationAsync(id, TestContext.Current.CancellationToken)).Succeeded);
        Assert.Contains("unbefristet", (await admin.GetAsync($"/verwaltung/vertraege/{id}")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_lease_in_same_period_is_rejected()
    {
        var (_, apartment, anna, ben) = await MasterDataAsync();
        await CreateLeaseAsync(apartment, Today, anna);

        var second = await App.WithScopeAsync(sp => sp.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = Today.AddMonths(3), TenantIds = [ben], BaseRent = 650m }, TestContext.Current.CancellationToken));

        Assert.False(second.Succeeded);
        Assert.Contains("Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.", string.Join(" ", second.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_change_move_out_new_address_and_move_in_prefilled()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, anna, ben) = await MasterDataAsync();
        var id = await CreateLeaseAsync(apartment, Today.AddMonths(-12), anna);
        var end = Today.AddDays(3);

        var moved = await App.WithScopeAsync(sp => sp.GetRequiredService<ILeaseService>().MoveOutAsync(
            id, new MoveOutInput { End = end, NewStreet = "Neue Straße 5", NewPostalCode = "10115", NewCity = "Berlin" }, TestContext.Current.CancellationToken));
        Assert.True(moved.Succeeded);

        // The move-out page links to the new lease with the day after the end prefilled.
        var moveOut = await admin.GetAsync($"/verwaltung/vertraege/{id}/auszug");
        Assert.Contains($"endet am {end:dd.MM.yyyy}", moveOut.Text, StringComparison.Ordinal);
        var moveIn = moveOut.Document.QuerySelectorAll("a").Single(a => a.TextContent == "Neuen Vertrag anlegen").GetAttribute("href")!;
        Assert.Contains($"beginn={Iso(end.AddDays(1))}", moveIn, StringComparison.Ordinal);

        await CreateLeaseAsync(apartment, end.AddDays(1), ben, 690m);
        var history = await admin.GetAsync($"/verwaltung/einheiten/{apartment}");
        Assert.Contains("Ben Weber", history.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Schmidt", history.Text, StringComparison.Ordinal);

        App.Clock.Advance(TimeSpan.FromDays(5)); // after the move-out the new address applies
        Assert.Contains("Neue Straße 5, 10115 Berlin", (await admin.GetAsync($"/verwaltung/personen/{anna}")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guarantor_can_be_added_and_removed_but_the_only_tenant_cannot()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var (_, apartment, anna, ben) = await MasterDataAsync();
        var id = await CreateLeaseAsync(apartment, Today, anna);

        await App.WithScopeAsync(async sp =>
        {
            var leases = sp.GetRequiredService<ILeaseService>();
            Assert.True((await leases.AddPartyAsync(id, ben, PartyRole.Guarantor, TestContext.Current.CancellationToken)).Succeeded);
            return 0;
        });
        Assert.Contains("Bürge", (await admin.GetAsync($"/verwaltung/vertraege/{id}")).Text, StringComparison.Ordinal);

        await App.WithScopeAsync(async sp =>
        {
            var leases = sp.GetRequiredService<ILeaseService>();
            Assert.True((await leases.RemovePartyAsync(id, ben, TestContext.Current.CancellationToken)).Succeeded);
            Assert.False((await leases.RemovePartyAsync(id, anna, TestContext.Current.CancellationToken)).Succeeded); // the only tenant
            return 0;
        });
        Assert.DoesNotContain("Bürge", (await admin.GetAsync($"/verwaltung/vertraege/{id}")).Text.Replace("Bürgen hinzufügen", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_master_data_permission_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        Assert.Equal("/konto/zugriff-verweigert", (await manager.GetAsync("/verwaltung/vertraege", followRedirects: false)).RedirectLocation!.AbsolutePath);
    }

    private async Task<(Guid Property, Guid Apartment, Guid Anna, Guid Ben)> MasterDataAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var people = sp.GetRequiredService<IPersonService>();
            var anna = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            var ben = (await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, ct)).Id!.Value;
            return (property, apartment, anna, ben);
        });
}
