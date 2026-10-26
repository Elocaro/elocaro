using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Properties;
using MietHahn.Domain.Properties;
using MietHahn.Web.Tests.Account;

namespace MietHahn.Web.Tests.Properties;

public class PropertiesTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    private static readonly int[] Numbers = [1, 2, 3, 4];

    // The data is created through the services; operating the interactive pages is covered by the browser tests.
    private async Task<Guid> CreateHouseAsync(string name = "Lindenweg 3", string street = "Lindenweg 3") =>
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = name, Street = street, PostalCode = "50667", City = "Köln", YearBuilt = 1965, EnergySource = EnergySource.NaturalGas, HeatingType = HeatingType.CentralHeating },
            TestContext.Current.CancellationToken)).Id!.Value);

    [Fact]
    public async Task Building_with_apartments_and_shared_flat_is_shown_in_overview_and_list()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;

        Assert.Contains("Noch keine Objekte", (await admin.GetAsync("/verwaltung/objekte")).Text, StringComparison.Ordinal);

        var propertyId = await CreateHouseAsync();
        var sharedFlat = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            Assert.True((await properties.SetAreaAsync(propertyId, new AreaInput { From = new DateOnly(2026, 1, 1), Area = 400, SecondaryArea = 30.5m }, ct)).Succeeded);
            Guid last = default;
            foreach (var number in Numbers)
            {
                last = (await properties.CreateUnitAsync(propertyId, new UnitInput { Name = $"Wohnung {number}", Location = number <= 2 ? "EG" : "OG" }, ct)).Id!.Value;
            }

            Assert.True((await properties.SetUnitAreaAsync(last, new AreaInput { From = new DateOnly(2026, 1, 1), Area = 110 }, ct)).Succeeded);
            foreach (var number in Numbers)
            {
                Assert.True((await properties.CreateRoomAsync(last, new RoomInput { Name = $"Zimmer {number}", Furnishing = Furnishing.Furnished }, ct)).Succeeded);
            }

            return last;
        });

        var property = await admin.GetAsync($"/verwaltung/objekte/{propertyId}");
        Assert.Contains("Lindenweg 3, 50667 Köln", property.Text, StringComparison.Ordinal);
        Assert.Contains("Zentralheizung, Erdgas", property.Text, StringComparison.Ordinal);
        Assert.Contains("30,5 m²", property.Text, StringComparison.Ordinal);
        Assert.Equal(7, property.Document.QuerySelectorAll("tbody .status-pending").Count(e => e.TextContent == "Leerstand"));
        Assert.Contains("WG", property.Text, StringComparison.Ordinal);
        // Without living area for apartments 1–3 and rooms: plausibility hints
        Assert.Contains("Wohnung 1: Keine Wohnfläche", property.Text, StringComparison.Ordinal);
        Assert.Contains("110 m²", (await admin.GetAsync($"/verwaltung/einheiten/{sharedFlat}")).Text, StringComparison.Ordinal);

        Assert.Contains("0 von 7 vermietet", (await admin.GetAsync("/verwaltung/objekte")).Text, StringComparison.Ordinal);

        // The house overview groups the units by floor, the shared flat shows its rooms, everything is empty.
        var house = (await admin.GetAsync($"/verwaltung/objekte/{propertyId}/belegung")).Document.QuerySelector("#house-overview")!;
        Assert.Equal(["EG", "OG"], house.QuerySelectorAll(".house-floor-name").Select(e => e.TextContent.Trim()).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(4, house.QuerySelectorAll(".house-unit .house-room.state-empty[title^='Zimmer']").Length);
    }

    [Fact]
    public async Task Area_change_from_effective_date_keeps_the_history_and_rejects_retroactive_dates()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var propertyId = await CreateHouseAsync();

        var (unitId, retroactive) = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var unit = (await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            Assert.True((await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2025, 1, 1), Area = 75.5m }, ct)).Succeeded);
            Assert.True((await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2026, 2, 1), Area = 82.4m, SecondaryArea = 80 }, ct)).Succeeded);
            return (unit, await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2025, 6, 1), Area = 70 }, ct));
        });

        var unitPage = await admin.GetAsync($"/verwaltung/einheiten/{unitId}");
        Assert.Contains("Verlauf", unitPage.Text, StringComparison.Ordinal);
        Assert.Contains("01.01.2025 – 31.01.2026", unitPage.Text, StringComparison.Ordinal);
        Assert.Contains("seit 01.02.2026", unitPage.Text, StringComparison.Ordinal);
        Assert.False(retroactive.Succeeded);
        Assert.Contains("Der Stichtag muss am oder nach dem 01.02.2026 liegen", string.Join(" ", retroactive.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Implausible_values_and_duplicate_names_are_rejected_by_the_services()
    {
        var propertyId = await CreateHouseAsync();
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var negative = await properties.SetAreaAsync(propertyId, new AreaInput { From = new DateOnly(2026, 1, 1), Area = -5 }, ct);
            Assert.Contains("Die Gesamtfläche muss größer als 0 sein.", string.Join(" ", negative.Errors), StringComparison.Ordinal);

            Assert.True((await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "W1" }, ct)).Succeeded);
            var duplicate = await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "w1" }, ct);
            Assert.Contains("Im Objekt gibt es bereits eine Einheit „w1“.", string.Join(" ", duplicate.Errors), StringComparison.Ordinal);
            return 0;
        });
    }

    [Fact]
    public async Task Without_master_data_permission_no_access()
    {
        var (manager, _) = await SignedInAsAsync(Roles.Manager, [AccessRight.Finance]);
        using var _ = manager;

        var response = await manager.GetAsync("/verwaltung/objekte", followRedirects: false);

        Assert.Equal("/konto/zugriff-verweigert", response.RedirectLocation!.AbsolutePath);
        Assert.DoesNotContain("/verwaltung/objekte", (await manager.GetAsync("/verwaltung")).Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caretaker_sees_only_assigned_properties()
    {
        var (admin, _) = await SignedInAsAsync(Roles.Admin);
        using var _ = admin;
        var idA = await CreateHouseAsync();
        await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IPropertyService>().CreateUnitAsync(idA, new UnitInput { Name = "Wohnung 1", Location = "EG links" }, TestContext.Current.CancellationToken)).Succeeded);
        var idB = await CreateHouseAsync("Birkenallee 9", "Birkenallee 9");

        var (caretaker, caretakerId) = await SignedInAsAsync(Roles.Caretaker);
        using var __ = caretaker;

        // Assignment (the page itself is covered by the browser tests); the account page lists the properties to choose from
        var account = await admin.GetAsync($"/verwaltung/benutzer/{caretakerId}");
        Assert.Contains("Betreute Objekte", account.Text, StringComparison.Ordinal);
        Assert.True((await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().SetAsync(caretakerId, [idA], TestContext.Current.CancellationToken))).Succeeded);
        Assert.Equal([idA], await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().PropertiesOfAsync(caretakerId, TestContext.Current.CancellationToken)));

        var list = await caretaker.GetAsync("/service/objekte");
        Assert.Contains("Lindenweg 3", list.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", list.Text, StringComparison.Ordinal);

        var allowed = await caretaker.GetAsync($"/service/objekte/{idA}");
        Assert.Contains("EG links", allowed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Leerstand", allowed.Text, StringComparison.Ordinal); // no occupancy/tenant data

        var forbidden = await caretaker.GetAsync($"/service/objekte/{idB}");
        Assert.Contains("Objekt nicht gefunden", forbidden.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Birkenallee", forbidden.Text, StringComparison.Ordinal);

        Assert.Equal("/konto/zugriff-verweigert", (await caretaker.GetAsync($"/verwaltung/objekte/{idA}", followRedirects: false)).RedirectLocation!.AbsolutePath);

        // Tenants have no property assignment
        var (_, tenantId) = await SignedInAsAsync(Roles.Tenant);
        var result = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyAssignments>().SetAsync(tenantId, [idA], TestContext.Current.CancellationToken));
        Assert.False(result.Succeeded);
    }
}
