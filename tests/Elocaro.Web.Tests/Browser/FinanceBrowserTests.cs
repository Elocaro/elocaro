using System.Globalization;
using Elocaro.Application.Documents;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Operating the interactive finance pages: settings, tenant account, dunning.</summary>
public class FinanceBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string TenantEmail = "anna@example.de";

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    private static string German(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static DateOnly FirstOfMonth(int offset) => new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(offset);

    private Task<Guid> LeaseIdAsync() =>
        App.WithScopeAsync(async sp => (await sp.GetRequiredService<ILeaseService>().ListAsync(true, TestContext.Current.CancellationToken)).Single().Id);

    private async Task<List<OutgoingMail>> MailsAsync()
    {
        var mails = new List<OutgoingMail>();
        await App.WithDbAsync(async db => mails.AddRange(await db.Set<OutgoingMail>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken)));
        return mails;
    }

    /// <summary>Lease from <paramref name="monthsAgo"/> months ago (unpaid), landlord profile and finance settings.</summary>
    private async Task SetUpAsync(int monthsAgo, FinanceSettingsInput settings, string? email = TenantEmail)
    {
        await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = email }, null, ct)).Id!.Value;
            await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = FirstOfMonth(-monthsAgo), TenantIds = [tenant], BaseRent = 650m }, ct);
            var db = sp.GetRequiredService<Elocaro.Infrastructure.Persistence.ElocaroDbContext>();
            db.Add(new LandlordProfile("Max Mustermann", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            await db.SaveChangesAsync(ct);
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(settings, ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, ct);
            return 0;
        });
    }

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await GotoInteractiveAsync(page, path);
        return page;
    }

    [Fact]
    public async Task Accounting_start_is_saved_creates_the_monthly_rents_and_payments_can_be_booked_and_deleted()
    {
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var previousMonth = FirstOfMonth(-1);
        var lease = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            var apartment = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1" }, ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, ct)).Id!.Value;
            return (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = apartment, Start = previousMonth.AddDays(14), TenantIds = [tenant], BaseRent = 650m }, ct)).Id!.Value;
        });
        var page = await OpenAsync("/verwaltung/finanzen/einstellungen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByLabel("Buchhaltung ab (Monat)").FillAsync(German(previousMonth));
            await page.GetByRole(AriaRole.Radio, new() { Name = "Gesetzlich" }).CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Gespeichert. 2 Sollstellungen angelegt oder angeglichen.");

            await GotoInteractiveAsync(page, $"/verwaltung/vertraege/{lease}/konto");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("(anteilig");

            await OpenTabAsync(page, "Zahlung & Forderung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Zahlung erfassen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByLabel("Datum", new() { Exact = true }).FillAsync(German(today));
            await dialog.GetByLabel("Betrag", new() { Exact = true }).FillAsync("100,5");
            await dialog.GetByLabel("Verwendungszweck").FillAsync("Miete");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Zahlung buchen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Zahlung wurde gebucht.");
            await Assertions.Expect(page.Locator("tbody").First).ToContainTextAsync("100,50 €");

            await OpenTabAsync(page, "Buchungen");
            // Monthly rents have no delete button, the payment does (after confirming).
            await Assertions.Expect(page.Locator("button[aria-label$='löschen']")).ToHaveCountAsync(1);
            await page.Locator("button[aria-label$='löschen']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, löschen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Buchung wurde gelöscht.");
            await Assertions.Expect(page.Locator("button[aria-label$='löschen']")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task Fee_interest_and_base_rate_are_maintained_and_invalid_values_are_reported()
    {
        var page = await OpenAsync("/verwaltung/finanzen/einstellungen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("keine Rechtsberatung");
            await Assertions.Expect(page.GetByLabel("Karenzzeit (Tage)")).ToHaveValueAsync("5");

            await page.GetByLabel("Karenzzeit (Tage)").FillAsync("3");
            await page.GetByLabel("Tage bis zur 1. Mahnung").FillAsync("10");
            await page.GetByLabel("Tage bis zur 2. Mahnung").FillAsync("12");
            await page.GetByLabel("Zahlungsfrist im Schreiben (Tage)").FillAsync("9");
            await page.GetByLabel("Mindestbetrag").FillAsync("10");
            await page.GetByLabel("Gebühr 1. Mahnung").FillAsync("5");
            await page.GetByLabel("Gebühr 2. Mahnung").FillAsync("7,5");
            await page.GetByLabel("Verzugszinsen ab der 1. Mahnung berechnen").CheckAsync();
            await page.GetByLabel("Basiszinssatz (% p. a.)").FillAsync("2,27");
            await page.GetByLabel("Basiszinssatz gültig seit").FillAsync("01.07.2026");
            await page.GetByLabel("Aufschlag (Prozentpunkte)").FillAsync("5");
            await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Gespeichert.");
        });

        var settings = await App.WithScopeAsync(sp => sp.GetRequiredService<IFinanceSettings>().GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, settings.DunningGraceDays);
        Assert.Equal(10, settings.DaysUntilFirstNotice);
        Assert.Equal(12, settings.DaysUntilSecondNotice);
        Assert.Equal(9, settings.DunningDeadlineDays);
        Assert.Equal(10m, settings.DunningMinimumAmount);
        Assert.Equal(5m, settings.FirstNoticeFee);
        Assert.Equal(7.5m, settings.SecondNoticeFee);
        Assert.True(settings.ChargeDefaultInterest);
        Assert.Equal(2.27m, settings.BaseRate);
        Assert.Equal(new DateOnly(2026, 7, 1), settings.BaseRateAsOf);
        Assert.Equal(5m, settings.InterestMarginPoints);

        await page.GetByLabel("Karenzzeit (Tage)").FillAsync("400");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern" }).ClickAsync();
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Die Karenzzeit liegt zwischen 0 und 60 Tagen.");
    }

    [Fact]
    public async Task Lease_term_is_corrected_in_a_dialog()
    {
        await SetUpAsync(monthsAgo: 3, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-4) });
        var lease = await LeaseIdAsync();
        var page = await OpenAsync($"/verwaltung/vertraege/{lease}");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Mietzeit korrigieren" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            var start = dialog.GetByLabel("Mietbeginn");
            await start.FillAsync(German(FirstOfMonth(-4)));
            await start.BlurAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Mietzeit wurde gespeichert.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync(German(FirstOfMonth(-4)));
        });
    }

    [Fact]
    public async Task Charges_are_booked_on_the_tenant_account()
    {
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        await SetUpAsync(monthsAgo: 3, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-3) });
        var lease = await LeaseIdAsync();
        var page = await OpenAsync($"/verwaltung/vertraege/{lease}/konto");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await OpenTabAsync(page, "Zahlung & Forderung");
            await page.GetByRole(AriaRole.Button, new() { Name = "Forderung oder Gutschrift buchen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await SelectAsync(page, "Art", "Nachzahlung (Abrechnung)", dialog);
            await dialog.GetByLabel("Fällig am").FillAsync(German(today.AddDays(-20)));
            await dialog.GetByLabel("Bezeichnung").FillAsync("Nachzahlung Betriebskosten 2025");
            var amount = dialog.GetByLabel("Betrag", new() { Exact = true });
            await amount.FillAsync("212,4");
            await amount.BlurAsync();
            await Assertions.Expect(amount).ToHaveValueAsync("212,4");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Buchen", Exact = true }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Buchung wurde angelegt.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Nachzahlung Betriebskosten 2025");
        });
    }

    [Fact]
    public async Task Approval_sends_the_reminder_by_mail_and_offers_the_pdf()
    {
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1), ReminderFee = 0m });
        var page = await OpenAsync("/verwaltung/finanzen/mahnungen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Basisvorlage");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Noch keine Mahnung freigegeben.");
            await page.GetByRole(AriaRole.Link, new() { Name = "prüfen und freigeben" }).ClickAsync();

            await page.WaitForURLAsync("**/verwaltung/finanzen/mahnungen/*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Es wird nichts versendet, bevor Sie „Freigeben“ wählen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync(TenantEmail);
            Assert.Empty(await MailsAsync());

            await page.GetByRole(AriaRole.Button, new() { Name = "Freigeben" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Mahnung wurde freigegeben und per E-Mail versendet.");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "PDF herunterladen" })).ToBeVisibleAsync();
        });

        var mail = Assert.Single(await MailsAsync());
        Assert.Equal([TenantEmail], mail.To);
        Assert.Equal("Zahlungserinnerung zu Ihrer Mietzahlung", mail.Subject);
        Assert.Single(mail.AttachmentFileIds);
    }

    [Fact]
    public async Task Approval_without_an_email_address_gives_a_download_for_postal_delivery()
    {
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) }, email: null);
        var page = await OpenAsync("/verwaltung/finanzen/mahnungen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "prüfen und freigeben" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/finanzen/mahnungen/*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("keine hinterlegt");

            await page.GetByRole(AriaRole.Button, new() { Name = "Freigeben" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Mahnung wurde freigegeben. Das PDF steht zum Download für den Postversand bereit.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("nicht per E-Mail versendet");
        });
        Assert.Empty(await MailsAsync());
    }

    [Fact]
    public async Task Payment_before_approval_removes_the_proposal_and_a_stale_approval_sends_nothing()
    {
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) });
        var lease = await LeaseIdAsync();
        var page = await OpenAsync("/verwaltung/finanzen/mahnungen");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            // A full page load (not a link click) so that the interactive marker really belongs to the dialog page.
            var dialog = await page.GetByRole(AriaRole.Link, new() { Name = "prüfen und freigeben" }).GetAttributeAsync("href");
            await GotoInteractiveAsync(page, dialog!);

            // The tenant pays everything while the dialog is open.
            await App.WithScopeAsync(async sp =>
            {
                var accounts = sp.GetRequiredService<ITenantAccounts>();
                var ct = TestContext.Current.CancellationToken;
                var open = (await accounts.GetAsync(lease, ct))!.Balance;
                Assert.True((await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = GermanTime.Today(App.Clock.GetUtcNow()), Amount = open }, ct)).Succeeded);
                return 0;
            });

            await page.GetByRole(AriaRole.Button, new() { Name = "Freigeben" }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Der Rückstand besteht nicht mehr");
        });

        Assert.Empty(await MailsAsync());
        await GotoInteractiveAsync(page, "/verwaltung/finanzen/mahnungen");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Keine Mahnvorschläge");
    }

    [Fact]
    public async Task Hold_stops_the_proposals_and_a_proposal_can_be_dismissed()
    {
        await SetUpAsync(monthsAgo: 1, new FinanceSettingsInput { AccountingStart = FirstOfMonth(-1) });
        var lease = await LeaseIdAsync();
        var page = await OpenAsync($"/verwaltung/vertraege/{lease}/konto");

        await WithScreenshotOnFailureAsync(page, async () =>
        {
            await Assertions.Expect(page.Locator("#mahnwesen")).ToContainTextAsync("Mahnvorschlag:");
            await OpenTabAsync(page, "Mahnwesen");
            await page.GetByRole(AriaRole.Button, new() { Name = "Mahnsperre setzen" }).ClickAsync();
            var dialog = page.Locator(".mud-dialog");
            await dialog.GetByLabel("Grund").FillAsync("Ratenvereinbarung");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Mahnsperre setzen" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Mahnsperre wurde gesetzt.");
            await Assertions.Expect(page.Locator("#mahnwesen")).ToContainTextAsync("Ratenvereinbarung");
            await Assertions.Expect(page.Locator("#mahnwesen")).Not.ToContainTextAsync("Mahnvorschlag:");

            await GotoInteractiveAsync(page, "/verwaltung/finanzen/mahnungen");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Keine Mahnvorschläge");
            await page.Locator("button[aria-label$='aufheben']").ClickAsync();
            await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, aufheben" }).ClickAsync();
            await ExpectSnackbarAsync(page, "Die Mahnsperre wurde aufgehoben.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Keine aktive Mahnsperre.");

            await page.GetByRole(AriaRole.Link, new() { Name = "prüfen und freigeben" }).ClickAsync();
            await page.WaitForURLAsync("**/verwaltung/finanzen/mahnungen/*");
            await Assertions.Expect(page.Locator("[data-interactive=true]")).ToHaveCountAsync(1);

            // After the navigation the marker of the previous page can still be there; a click before the circuit of this page is ready is lost.
            for (var attempt = 1; ; attempt++)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Vorschlag verwerfen" }).ClickAsync();
                try
                {
                    await page.Locator(".mud-message-box").GetByRole(AriaRole.Button, new() { Name = "Ja, verwerfen" }).ClickAsync(new() { Timeout = 8000 });
                    break;
                }
                catch (TimeoutException) when (attempt < 3)
                {
                }
            }

            await ExpectSnackbarAsync(page, "Der Vorschlag wurde verworfen.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("wurde am");
        });
    }
}
