using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.Finance;

namespace MietHahn.Web.Tests.Browser;

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
}
