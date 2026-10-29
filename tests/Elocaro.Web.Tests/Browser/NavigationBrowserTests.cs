using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Orientation in the administration: the trail with names and the workspace tabs of the property on its sub-pages.</summary>
public class NavigationBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static readonly string[] PropertyTabPaths = ["", "/belegung", "/zaehler"];

    private async Task<(Guid Property, Guid Unit, Guid Room, Guid Lease, Guid Person)> SampleAsync() =>
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 4" }, ct)).Id!.Value;
            var room = (await properties.CreateRoomAsync(unit, new RoomInput { Name = "Zimmer 1" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var person = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(
                new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2026, 1, 1), TenantIds = [person], BaseRent = 650m }, ct)).Id!.Value;
            return (property, unit, room, lease, person);
        });

    [Fact]
    public async Task Trail_names_the_property_and_the_way_back_works_from_room_lease_and_person_on_desktop()
    {
        var (property, unit, room, lease, person) = await SampleAsync();
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var trail = page.GetByRole(AriaRole.Navigation, new() { Name = "Brotkrumenpfad" });

            await page.GotoAsync(BaseUrl + $"/verwaltung/zimmer/{room}");
            await Assertions.Expect(trail).ToContainTextAsync("Lindenweg 3");
            await Assertions.Expect(trail).ToContainTextAsync("Wohnung 4");
            await Assertions.Expect(trail).ToContainTextAsync("Zimmer 1");
            // The room keeps the tabs of its property, not the generic "Bestand" tabs.
            await Assertions.Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Objekt", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "Belegung" })).ToBeVisibleAsync();

            await trail.GetByRole(AriaRole.Link, new() { Name = "Wohnung 4" }).ClickAsync();
            await page.WaitForURLAsync($"**/verwaltung/einheiten/{unit}");

            await page.GotoAsync(BaseUrl + $"/verwaltung/vertraege/{lease}/kommunikation");
            await Assertions.Expect(trail).ToContainTextAsync("Lindenweg 3");
            await Assertions.Expect(trail).ToContainTextAsync("Schmidt");
            await trail.GetByRole(AriaRole.Link, new() { Name = "Lindenweg 3" }).ClickAsync();
            await page.WaitForURLAsync($"**/verwaltung/objekte/{property}");

            await page.GotoAsync(BaseUrl + $"/verwaltung/personen/{person}");
            await Assertions.Expect(trail).ToContainTextAsync("Anna Schmidt");
            await trail.GetByRole(AriaRole.Link, new() { Name = "Personen" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/personen");
        });
    }

    [Fact]
    public async Task Property_header_sits_above_the_tabs_and_content_lines_up_with_them_on_wide_screens()
    {
        var (property, _, _, _, _) = await SampleAsync();
        var page = await SignedInAsAdminAsync(mobile: false);
        await page.SetViewportSizeAsync(1920, 1000);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            foreach (var path in PropertyTabPaths)
            {
                await page.GotoAsync(BaseUrl + $"/verwaltung/objekte/{property}{path}");
                var header = page.GetByRole(AriaRole.Region, new() { Name = "Objekt", Exact = true });
                await Assertions.Expect(header).ToContainTextAsync("Lindenweg 3");
                await Assertions.Expect(header).ToContainTextAsync("50667 Köln");

                var tabs = page.GetByRole(AriaRole.Navigation, new() { Name = "Objekt", Exact = true });
                var headerBox = await header.BoundingBoxAsync();
                var tabsBox = await tabs.BoundingBoxAsync();
                Assert.True(headerBox!.Y < tabsBox!.Y, $"Header above tabs on {path}");

                var title = await page.Locator("main h1").BoundingBoxAsync();
                Assert.InRange(title!.X - headerBox.X, 0, 40);
            }
        });
    }

    [Fact]
    public async Task Trail_fits_360_pixels_and_keeps_the_parent_level()
    {
        var (_, _, room, lease, _) = await SampleAsync();
        var page = await SignedInAsAdminAsync();

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            var trail = page.GetByRole(AriaRole.Navigation, new() { Name = "Brotkrumenpfad" });
            foreach (var path in new[] { $"/verwaltung/zimmer/{room}", $"/verwaltung/vertraege/{lease}" })
            {
                await page.GotoAsync(BaseUrl + path);
                await Assertions.Expect(trail).ToBeVisibleAsync();
                await NoHorizontalScrollAsync(page);
            }

            // On the phone only the parent and the page itself remain.
            await Assertions.Expect(trail.GetByRole(AriaRole.Link, new() { Name = "Lindenweg 3" })).ToBeVisibleAsync();
            await Assertions.Expect(trail.GetByRole(AriaRole.Link, new() { Name = "Verträge" })).ToBeHiddenAsync();
        });
    }
}
