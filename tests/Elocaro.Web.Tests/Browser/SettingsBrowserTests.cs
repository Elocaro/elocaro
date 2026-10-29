using Elocaro.Application.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the landlord profile page.</summary>
public class SettingsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static async Task FillValidAsync(IPage page)
    {
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Max Mustermann");
        await page.GetByLabel("Straße und Hausnummer").FillAsync("Musterweg 1");
        await page.GetByLabel("PLZ").FillAsync("12345");
        await page.GetByLabel("Ort").FillAsync("Musterstadt");
        await page.GetByLabel("IBAN").FillAsync("de02120300000000202051");
        await page.GetByLabel("BIC").FillAsync("byladem1001");
    }

    [Fact]
    public async Task Landlord_profile_is_created_normalized_and_updated()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/einstellungen/vermieter");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch kein Vermieterprofil hinterlegt");

            await FillValidAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Vermieterangaben wurden gespeichert.");
            await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("Noch kein Vermieterprofil hinterlegt");

            await page.GetByLabel("Ort").FillAsync("Neustadt");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Vermieterangaben wurden gespeichert.");
        });

        // The second snackbar looks like the first one; wait for the change itself.
        LandlordProfileInput? profile = null;
        for (var attempt = 0; attempt < 50 && profile?.City != "Neustadt"; attempt++)
        {
            profile = await App.WithScopeAsync(sp => sp.GetRequiredService<ILandlordProfileService>().GetAsync(TestContext.Current.CancellationToken));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal("Neustadt", profile!.City);
        Assert.Equal("DE02 1203 0000 0000 2020 51", profile.Iban);
        Assert.Equal("BYLADEM1001", profile.Bic);
    }

    [Fact]
    public async Task Invalid_postal_code_and_email_are_reported_and_nothing_is_saved()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, "/verwaltung/einstellungen/vermieter");

        await FillValidAsync(page);
        await page.GetByLabel("PLZ").FillAsync("1234");
        await page.GetByLabel("E-Mail").FillAsync("kein-at");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();

        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Die Postleitzahl hat 5 Ziffern.");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte eine gültige E-Mail-Adresse angeben.");
        Assert.Null(await App.WithScopeAsync(sp => sp.GetRequiredService<ILandlordProfileService>().GetAsync(TestContext.Current.CancellationToken)));

        // An empty e-mail address is allowed.
        await page.GetByLabel("PLZ").FillAsync("12345");
        await page.GetByLabel("E-Mail").FillAsync("");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
        await ExpectSnackbarAsync(page, "Die Vermieterangaben wurden gespeichert.");
    }
}
