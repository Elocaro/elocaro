using Elocaro.Application.Admin;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Viewing dates: the landlord offers a date and a link, a prospect without an account books it, the booking shows up for the landlord.</summary>
public class ViewingBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task A_prospect_books_an_offered_date_over_the_link_and_the_landlord_sees_the_booking()
    {
        await App.WithScopeAsync<bool>(async sp =>
        {
            await sp.GetRequiredService<IModuleState>().SaveAsync(Enum.GetValues<AppModule>(), true, null, TestContext.Current.CancellationToken);
            var property = (await sp.GetRequiredService<IPropertyService>().CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value;
            await sp.GetRequiredService<IPropertyService>().CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, TestContext.Current.CancellationToken);
            return true;
        });
        var day = DateOnly.FromDateTime(DateTime.Now.AddDays(10));
        var admin = await SignedInAsAdminAsync(mobile: false);
        var token = "";

        await WithScreenshotOnFailureAsync(admin, async () =>
        {
            await GotoInteractiveAsync(admin, "/verwaltung/besichtigungen");
            await SelectAsync(admin, "Mietobjekt", "Wohnung 1, Lindenweg 3");
            await admin.GetByLabel("Tag").FillAsync(day.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture));
            await admin.GetByLabel("Uhrzeit").FillAsync("10:30");
            await admin.GetByRole(AriaRole.Button, new() { Name = "Termin anlegen" }).ClickAsync();
            await Assertions.Expect(admin.Locator("main")).ToContainTextAsync("10:30 Uhr");
            await Assertions.Expect(admin.Locator("main")).ToContainTextAsync("frei");

            await admin.GetByRole(AriaRole.Button, new() { Name = "Link für die Einheit erzeugen" }).ClickAsync();
            var link = admin.GetByLabel("Link zur Terminbuchung");
            await Assertions.Expect(link).ToBeVisibleAsync();
            var url = await link.InputValueAsync();
            token = url[(url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            Assert.NotEmpty(token);
        });

        var guest = await NewPageAsync(mobile: true);
        await WithScreenshotOnFailureAsync(guest, async () =>
        {
            await guest.GotoAsync($"{BaseUrl}/besichtigung/{token}");
            await Assertions.Expect(guest.Locator("h1")).ToHaveTextAsync("Besichtigung buchen");
            await guest.GetByLabel("Termin", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Index = 1 });
            await guest.GetByLabel("Ihr Name").FillAsync("Ben Weber");
            await guest.GetByLabel("E-Mail-Adresse oder Telefonnummer", new() { Exact = false }).FillAsync("ben@example.org");
            await guest.GetByRole(AriaRole.Button, new() { Name = "Termin buchen" }).ClickAsync();

            await Assertions.Expect(guest.Locator("h1")).ToHaveTextAsync("Ihr Termin ist gebucht");
            await Assertions.Expect(guest.GetByRole(AriaRole.Link, new() { Name = "Termin in den Kalender übernehmen" })).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("^data:text/calendar"));
            await NoHorizontalScrollAsync(guest);
        });

        await WithScreenshotOnFailureAsync(admin, async () =>
        {
            await GotoInteractiveAsync(admin, "/verwaltung/besichtigungen");
            await Assertions.Expect(admin.Locator("main")).ToContainTextAsync("Ben Weber");
            await GotoInteractiveAsync(admin, "/verwaltung/interessenten");
            await Assertions.Expect(admin.Locator("main")).ToContainTextAsync("Ben Weber");
            await Assertions.Expect(admin.Locator("main")).ToContainTextAsync("Besichtigung");
        });

        // A second visitor with the same link no longer sees the date.
        var other = await NewPageAsync(mobile: true);
        await other.GotoAsync($"{BaseUrl}/besichtigung/{token}");
        await Assertions.Expect(other.Locator("main")).ToContainTextAsync("keine Termine frei");
    }
}
