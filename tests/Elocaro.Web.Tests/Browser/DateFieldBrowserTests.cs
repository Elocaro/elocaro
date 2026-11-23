using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Typing dates and months: format hint, entry without dots, unusable input is reported instead of dropped.</summary>
public class DateFieldBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    [Fact]
    public async Task Date_field_shows_the_format_and_formats_digits_while_typing()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/vertraege/neu");
            var start = page.GetByLabel("Mietbeginn");
            await Assertions.Expect(start).ToHaveAttributeAsync("placeholder", "TT.MM.JJJJ");

            await start.FillAsync("01112026");
            await start.PressAsync("Tab");
            await Assertions.Expect(start).ToHaveValueAsync("01.11.2026");

            await start.FillAsync("1.2.27");
            await start.PressAsync("Tab");
            await Assertions.Expect(start).ToHaveValueAsync("01.02.2027");

            await start.FillAsync("31.02.2026");
            await start.PressAsync("Tab");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Bitte ein Datum wie 01.11.2026 eingeben.");
        });
    }

    [Fact]
    public async Task Accounting_start_is_entered_as_month_and_saved_as_first_day_of_the_month()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/finanzen/einstellungen");
            var month = page.GetByLabel("Buchhaltung ab (Monat)");
            await Assertions.Expect(month).ToHaveAttributeAsync("placeholder", "MM.JJJJ");

            await month.FillAsync("112026");
            await month.PressAsync("Tab");
            await Assertions.Expect(month).ToHaveValueAsync("11.2026");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await Assertions.Expect(page.Locator("[role=status], .mud-snackbar").First).ToContainTextAsync("gespeichert", new() { IgnoreCase = true });
        });

        FinanceSettingsInput? settings = null;
        for (var attempt = 0; attempt < 50 && settings?.AccountingStart is null; attempt++)
        {
            settings = await App.WithScopeAsync(sp => sp.GetRequiredService<IFinanceSettings>().GetAsync());
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(new DateOnly(2026, 11, 1), settings!.AccountingStart);
    }

    /// <summary>The calendar reports the text of a chosen day before the date; the field must not hand the old (empty) date back in between.</summary>
    [Fact]
    public async Task A_day_chosen_in_the_calendar_is_taken_over_and_the_clear_button_empties_the_field()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/vertraege/neu");
            var field = page.Locator(".app-field").Filter(new() { Has = page.GetByLabel("Mietbeginn") });
            var input = field.Locator("input");

            await field.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
            await PickDayAsync(page, 15);
            await Assertions.Expect(input).ToHaveValueAsync($"15.{DateTime.Now:MM.yyyy}");
            await Assertions.Expect(page.Locator(".mud-popover-open")).ToHaveCountAsync(0);

            await field.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
            await Assertions.Expect(input).ToHaveValueAsync("");

            // Typing next to the calendar still works.
            await input.FillAsync("01112026");
            await input.PressAsync("Tab");
            await Assertions.Expect(input).ToHaveValueAsync("01.11.2026");
        });
    }

    [Fact]
    public async Task A_day_chosen_in_the_calendar_of_a_dialog_is_taken_over()
    {
        var lease = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            return (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2026, 1, 1), TenantIds = [tenant], BaseRent = 650m }, ct)).Id!.Value;
        });
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{lease}/konto");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zahlung erfassen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Open" }).First.ClickAsync();
            await PickDayAsync(page, 15);

            await Assertions.Expect(dialog.Locator(".app-field").First.Locator("input")).ToHaveValueAsync($"15.{DateTime.Now:MM.yyyy}");
        });
    }

    [Fact]
    public async Task A_month_chosen_in_the_calendar_is_taken_over()
    {
        var page = await SignedInAsAdminAsync(mobile: false);

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await GotoInteractiveAsync(page, "/verwaltung/finanzen/einstellungen");
            var field = page.Locator(".app-field").Filter(new() { Has = page.GetByLabel("Buchhaltung ab (Monat)") });

            await field.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
            await page.Locator(".mud-picker-month").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"^\s*(Mär|März)\s*$") }).First.ClickAsync();

            await Assertions.Expect(field.Locator("input")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^03\.\d{4}$"));
        });
    }

    private static async Task PickDayAsync(IPage page, int day)
    {
        await Assertions.Expect(page.Locator(".mud-picker-calendar")).ToHaveCountAsync(1);
        await page.Locator(".mud-picker-calendar-container button.mud-picker-calendar-day")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex($@"^\s*{day}\s*$") }).First.ClickAsync();
    }
}
