using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Application.People;
using MietHahn.Application.Properties;

namespace MietHahn.Web.Tests.Browser;

public class MasterDataBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Property_pages_fit_360_pixels()
    {
        var page = await SignedInAsAdminAsync();
        var (propertyId, sharedFlatId, roomId) = await SampleHouseAsync();

        foreach (var path in new[] { "/verwaltung/objekte", "/verwaltung/objekte/neu", $"/verwaltung/objekte/{propertyId}", $"/verwaltung/einheiten/{sharedFlatId}", $"/verwaltung/zimmer/{roomId}" })
        {
            await page.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(page);
        }
    }

    [Fact]
    public async Task Enter_area_in_a_dialog_with_German_notation_and_save()
    {
        var page = await SignedInAsAdminAsync();
        var (_, sharedFlatId, _) = await SampleHouseAsync();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GotoAsync(BaseUrl + $"/verwaltung/einheiten/{sharedFlatId}");
            await page.Locator("[data-interactive=true]").WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });
            await OpenTabAsync(page, "Flächen");
            await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Fläche ändern" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByLabel("Gültig ab").FillAsync("01.07.2026");
            await dialog.GetByLabel("Wohnfläche").FillAsync("112,5");
            await dialog.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Fläche speichern" }).ClickAsync();
            await page.WaitForSelectorAsync("text=Die Fläche wurde gespeichert.");
        });

        var unit = await App.WithScopeAsync(sp => sp.GetRequiredService<IPropertyService>().GetUnitAsync(sharedFlatId, TestContext.Current.CancellationToken));
        Assert.Equal(112.5m, unit!.AreaHistory[0].Area);
    }

    [Fact]
    public async Task People_pages_fit_360_pixels()
    {
        var page = await SignedInAsAdminAsync();
        var personId = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var v = sp.GetRequiredService<IPersonService>();
            var id = (await v.CreateAsync(
                new PersonInput { FirstName = "Maximiliane", LastName = "Mustermann-Schnarrenberger", Email = "maximiliane.mustermann-schnarrenberger@example.de", Phone = "0221 12345678" },
                new AddressInput { From = new DateOnly(2026, 1, 1), Street = "Sehr lange Straßenbezeichnung 123a", PostalCode = "50667", City = "Köln", AddressLine2 = "c/o Familie Mustermann" },
                ct)).Id!.Value;
            await v.AddBankDetailsAsync(id, new BankDetailsInput { Iban = "DE89370400440532013000", AccountHolder = "Maximiliane Mustermann-Schnarrenberger" }, ct);
            return id;
        });

        foreach (var path in new[] { "/verwaltung/personen", "/verwaltung/personen/neu", $"/verwaltung/personen/{personId}", "/verwaltung/datenschutz" })
        {
            await page.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(page);
        }
    }

    [Fact]
    public async Task Lease_pages_fit_360_pixels()
    {
        var page = await SignedInAsAdminAsync();
        var (_, _, room) = await SampleHouseAsync();
        var leaseId = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Maximiliane", LastName = "Mustermann-Schnarrenberger" }, null, ct)).Id!.Value;
            var leases = sp.GetRequiredService<ILeaseService>();
            var id = (await leases.CreateAsync(new LeaseCreation
            {
                RentalUnitId = room,
                Start = new DateOnly(2026, 1, 1),
                TenantIds = [tenant],
                BillingType = MietHahn.Domain.Leases.BillingType.FlatRate,
                BaseRent = 380,
                OperatingCostFlatRate = 120,
            }, ct)).Id!.Value;
            await leases.SetComponentAsync(id, new ComponentInput { Kind = MietHahn.Domain.Leases.RentComponentType.BaseRent, From = new DateOnly(2026, 4, 1), Amount = 400 }, ct);
            return id;
        });

        foreach (var path in new[] { "/verwaltung/vertraege", "/verwaltung/vertraege/neu", $"/verwaltung/vertraege/{leaseId}", $"/verwaltung/vertraege/{leaseId}/auszug", $"/verwaltung/zimmer/{room}" })
        {
            await page.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(page);
        }
    }

    [Fact]
    public async Task Reading_round_on_mobile_for_the_whole_building()
    {
        var page = await SignedInAsAdminAsync();
        var (property, _, _) = await SampleHouseAsync();
        var meterIds = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var meter = sp.GetRequiredService<IMeterService>();
            var ids = new List<Guid>();
            // Main water, common electricity, gas, per apartment water + heat (shared flat: 1×) = 3 + 4 + 1 = 8 meters
            foreach (var (name, type) in new[] { ("Hauptwasserzähler", MietHahn.Domain.Metering.MeterType.ColdWater), ("Allgemeinstrom", MietHahn.Domain.Metering.MeterType.Electricity), ("Gas Heizung", MietHahn.Domain.Metering.MeterType.Gas), ("Wasser Wohnung 1", MietHahn.Domain.Metering.MeterType.ColdWater), ("Wärme Wohnung 1", MietHahn.Domain.Metering.MeterType.Heat), ("Wasser Wohnung 2", MietHahn.Domain.Metering.MeterType.ColdWater), ("Wärme Wohnung 2", MietHahn.Domain.Metering.MeterType.Heat), ("Wasser WG", MietHahn.Domain.Metering.MeterType.ColdWater) })
            {
                ids.Add((await meter.CreateAsync(property, new MeterCreation
                {
                    Data = new MeterInput { Type = type, Name = name, Number = $"Nr-{ids.Count + 1}", Location = "Keller, Hausanschlussraum links" },
                    InstalledOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
                    InitialReading = 100,
                }, ct)).Id!.Value);
            }

            return ids;
        });

        var duration = System.Diagnostics.Stopwatch.StartNew();
        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/objekte/{property}/ablesung");
            for (var i = 0; i < meterIds.Count; i++)
            {
                await page.WaitForSelectorAsync($"text=Zähler {i + 1} von {meterIds.Count}");
                await NoHorizontalScrollAsync(page);
                await page.GetByLabel("Zählerstand").FillAsync((110 + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
                await page.ClickAsync("button:has-text('Speichern & weiter')");
            }

            await page.WaitForSelectorAsync("text=Runde abgeschlossen: 8 von 8 Zählern erfasst.");
        });
        duration.Stop();

        Assert.True(duration.Elapsed < TimeSpan.FromMinutes(1), $"Runde dauerte {duration.Elapsed}");
        foreach (var path in new[] { $"/verwaltung/objekte/{property}/zaehler", $"/verwaltung/zaehler/{meterIds[0]}" })
        {
            await page.GotoAsync(BaseUrl + path);
            await NoHorizontalScrollAsync(page);
        }
    }

    private async Task<(Guid Property, Guid SharedFlat, Guid Room)> SampleHouseAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var v = sp.GetRequiredService<IPropertyService>();
            var start = new DateOnly(2026, 1, 1);
            var property = (await v.CreateAsync(new PropertyInput { Name = "Lindenweg 3 mit langem Namen", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            await v.SetAreaAsync(property, new AreaInput { From = start, Area = 400, SecondaryArea = 30 }, ct);
            Guid sharedFlat = default, room = default;
            for (var i = 1; i <= 4; i++)
            {
                var unit = (await v.CreateUnitAsync(property, new UnitInput { Name = $"Wohnung {i}", Location = "1. OG rechts hinten" }, ct)).Id!.Value;
                await v.SetUnitAreaAsync(unit, new AreaInput { From = start, Area = 90 }, ct);
                sharedFlat = unit;
            }

            for (var z = 1; z <= 4; z++)
            {
                room = (await v.CreateRoomAsync(sharedFlat, new RoomInput { Name = $"Zimmer {z}" }, ct)).Id!.Value;
                await v.SetRoomAreaAsync(room, new AreaInput { From = start, Area = 14, SecondaryArea = 8 }, ct);
            }

            return (property, sharedFlat, room);
        });
}
