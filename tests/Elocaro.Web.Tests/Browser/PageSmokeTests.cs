using Elocaro.Application.Bank;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Documents;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Metering;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Application.Tax;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Common;
using Elocaro.Domain.Metering;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// Opens every page of the administration with realistic data (desktop and 360 px) and reports server errors, error pages,
/// console and script errors, failed requests and horizontal scrolling.
/// </summary>
public abstract class PageSweepBase(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected Dictionary<string, string> Ids { get; set; } = [];

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    protected async Task SeedAsync()
    {
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var start = new DateOnly(today.Year - 1, 1, 1);
        Ids = await App.WithScopeAsync(async sp =>
        {
            var result = new Dictionary<string, string>();
            var properties = sp.GetRequiredService<IPropertyService>();
            var property = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(property, new UnitInput { Name = "Wohnung 1", Location = "EG links" }, Ct)).Id!.Value;
            var shared = (await properties.CreateUnitAsync(property, new UnitInput { Name = "WG" }, Ct)).Id!.Value;
            var room = (await properties.CreateRoomAsync(shared, new RoomInput { Name = "Zimmer 1" }, Ct)).Id!.Value;
            await properties.SetUnitAreaAsync(unit, new AreaInput { From = start, Area = 60m }, Ct);
            await properties.SetUnitAreaAsync(shared, new AreaInput { From = start, Area = 40m }, Ct);

            var people = sp.GetRequiredService<IPersonService>();
            var person = (await people.CreateAsync(new PersonInput { Salutation = Domain.People.Salutation.Mrs, FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de", Phone = "0221 123456" },
                new AddressInput { From = start, Street = "Altweg 1", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            await people.AddBankDetailsAsync(person, new BankDetailsInput { Iban = "DE89370400440532013000" }, Ct);
            await people.CreateAsync(new PersonInput { LastName = "Klempner", Company = "Rohr & Co.", ContactType = Domain.People.ContactType.Tradesperson, Occupation = "Sanitär" }, null, Ct);

            var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(
                new LeaseCreation { RentalUnitId = unit, Start = start, TenantIds = [person], BaseRent = 650m, OperatingCostPrepayment = 120m, HeatingCostPrepayment = 40m }, Ct)).Id!.Value;
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = start }, Ct);
            await sp.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, Ct);

            await sp.GetRequiredService<IDocumentTemplates>().InstallDefaultsAsync(Ct);
            var meters = sp.GetRequiredService<IMeterService>();
            var meter = (await meters.CreateAsync(property,
                new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "KW-1", AssignmentId = unit }, InstalledOn = start, InitialReading = 0m }, Ct)).Id!.Value;
            await meters.RecordReadingAsync(meter, new ReadingInput { Date = new DateOnly(today.Year - 1, 12, 31), Value = 85m, Confirmed = true }, null, Ct);

            var receipts = sp.GetRequiredService<IReceiptService>();
            var receipt = (await receipts.CreateAsync(
                new ReceiptInput { SupplierName = "Stadt Köln", InvoiceNumber = "G-1", InvoiceDate = new DateOnly(today.Year - 1, 2, 1), Gross = 1000m, PaidOn = new DateOnly(today.Year - 1, 2, 10) },
                [],
                new CostItemInput { CostType = "betrkv-01", PropertyId = property, From = start, To = new DateOnly(today.Year - 1, 12, 31) },
                Ct)).Id!.Value;

            var loans = sp.GetRequiredService<ILoanService>();
            await loans.CreateAsync(new LoanInput
            {
                PropertyId = property,
                Bank = "Sparkasse",
                Principal = 200_000m,
                DisbursementDate = start,
                FirstPaymentDate = start.AddMonths(1),
                InterestRatePercent = 3.6m,
                Instalment = 1000m,
                MonthsPerPeriod = 1,
                SharePercent = 100m,
            }, Ct);
            result["loan"] = (await loans.ListAsync(Ct)).Single().Id.ToString();

            var bank = sp.GetRequiredService<IBankService>();
            var account = (await bank.CreateAccountAsync(new BankAccountInput { Name = "Mietkonto", Iban = "DE12500105170648489890", PropertyIds = [property] }, Ct)).Id!.Value;
            await sp.GetRequiredService<IMatchRules>().SaveAsync(null, new MatchRuleInput { Name = "Stadt Köln", CounterpartyContains = "Stadt Köln", CostType = "betrkv-01", PropertyId = property }, Ct);
            result["rule"] = (await sp.GetRequiredService<IMatchRules>().ListAsync(Ct)).Single().Id.ToString();

            var db = sp.GetRequiredService<ElocaroDbContext>();
            db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Iban = "DE02 1203 0000 0000 2020 51" });
            var open = new Transaction(account, null, new ParsedTransaction(today.AddDays(-5), 650m, "Miete Anna Schmidt", CounterpartyName: "Anna Schmidt", CounterpartyIban: "DE89370400440532013000"), Guid.NewGuid().ToString("N"));
            var expense = new Transaction(account, null, new ParsedTransaction(today.AddDays(-9), -1000m, "Grundsteuer", CounterpartyName: "Stadt Köln"), Guid.NewGuid().ToString("N"));
            db.AddRange(open, expense);
            await db.SaveChangesAsync(Ct);
            result["transaction"] = open.Id.ToString();
            result["account"] = account.ToString();

            var statement = await sp.GetRequiredService<IStatements>().ReleaseAsync(property, Period.CalendarYear(today.Year - 1), true, "Test", Ct);
            if (statement.Succeeded)
            {
                result["statement"] = statement.Id!.Value.ToString();
            }

            await sp.GetRequiredService<IDunning>().RefreshAsync(null, Ct);
            var dunnings = await sp.GetRequiredService<IDunning>().OverviewAsync(Ct);
            if (dunnings.Proposals.Count > 0)
            {
                result["dunning"] = dunnings.Proposals[0].Id.ToString();
            }

            // A draft handover protocol so that the mobile form is rendered with data.
            await sp.GetRequiredService<IHandovers>().CreateAsync(lease, Elocaro.Domain.Leases.HandoverType.MoveIn, today, Ct);

            result["task"] = (await sp.GetRequiredService<IDeadlines>().CreateTaskAsync(
                new TaskInput { Title = "Rauchmelder prüfen", Kind = Domain.Deadlines.TaskKind.SmokeDetectors, PropertyId = property, DueOn = today.AddDays(20), IntervalMonths = 12 }, Ct)).Id!.Value.ToString();

            result["property"] = property.ToString();
            result["unit"] = unit.ToString();
            result["room"] = room.ToString();
            result["person"] = person.ToString();
            result["lease"] = lease.ToString();
            result["meter"] = meter.ToString();
            result["receipt"] = receipt.ToString();
            result["year"] = (today.Year - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return result;
        });

        Ids["user"] = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<UserManager<ApplicationUser>>().Users.FirstAsync(Ct)).Id.ToString());
        Ids["import"] = await App.WithScopeAsync(async sp => (await sp.GetRequiredService<IBankService>().ImportsAsync(Guid.Parse(Ids["account"]), Ct)) is { Count: > 0 } imports ? imports[0].Id.ToString() : "");
    }

    protected IEnumerable<string> Routes()
    {
        string p = Ids["property"], year = Ids["year"];
        return
        [
            "/verwaltung", "/verwaltung/personen", "/verwaltung/personen?q=Schmidt", "/verwaltung/personen/neu", $"/verwaltung/personen/{Ids["person"]}",
            "/verwaltung/datenschutz", "/verwaltung/hilfe",
            "/verwaltung/objekte", "/verwaltung/objekte/neu", $"/verwaltung/objekte/{p}", $"/verwaltung/einheiten/{Ids["unit"]}", $"/verwaltung/zimmer/{Ids["room"]}",
            "/verwaltung/vertraege", "/verwaltung/vertraege/neu", $"/verwaltung/vertraege/{Ids["lease"]}", $"/verwaltung/vertraege/{Ids["lease"]}/konto", $"/verwaltung/vertraege/{Ids["lease"]}/auszug",
            $"/verwaltung/vertraege/{Ids["lease"]}/kostenarten", $"/verwaltung/vertraege/{Ids["lease"]}/vertragsdokument", $"/verwaltung/vertraege/{Ids["lease"]}/mieterhoehung", $"/verwaltung/vertraege/{Ids["lease"]}/uebergabe/einzug", $"/verwaltung/vertraege/{Ids["lease"]}/uebergabe/auszug", "/verwaltung/vpi",
            "/verwaltung/dokumente", "/verwaltung/dokumente/neu", "/verwaltung/tickets/routinen", "/verwaltung/aushaenge", "/verwaltung/aushaenge/neu", "/verwaltung/nachrichten", "/verwaltung/nachrichten/neu",
            "/verwaltung/fristen", "/verwaltung/fristen/wartung", $"/verwaltung/fristen/wartung/{Ids["task"]}", "/verwaltung/einstellungen/fristen", "/verwaltung/einstellungen/rechtsstaende", "/verwaltung/einstellungen/import",
            $"/verwaltung/objekte/{p}/zaehler", $"/verwaltung/objekte/{p}/ablesung", $"/verwaltung/zaehler/{Ids["meter"]}",
            "/verwaltung/bank/konten", "/verwaltung/bank/konten/neu", $"/verwaltung/bank/konten/{Ids["account"]}", "/verwaltung/bank/umsaetze", "/verwaltung/bank/zuordnung",
            $"/verwaltung/bank/umsaetze/{Ids["transaction"]}/zuordnen", "/verwaltung/bank/regeln", "/verwaltung/bank/regeln/neu", $"/verwaltung/bank/regeln/{Ids["rule"]}", "/verwaltung/bank/csv-profile",
            "/verwaltung/finanzen/offene-posten", "/verwaltung/finanzen/mahnungen", "/verwaltung/finanzen/einstellungen", "/verwaltung/finanzen/darlehen", $"/verwaltung/finanzen/darlehen/{Ids["loan"]}",
            "/verwaltung/belege", "/verwaltung/belege/neu", $"/verwaltung/belege/{Ids["receipt"]}",
            $"/verwaltung/objekte/{p}/anlage-v?jahr={year}", "/verwaltung/finanzen/anlage-v-zeilen?jahr=" + year, "/verwaltung/finanzen/datev", $"/verwaltung/objekte/{p}/afa", $"/verwaltung/objekte/{p}/erhaltungsaufwand",
            $"/verwaltung/objekte/{p}/abrechnungen", $"/verwaltung/objekte/{p}/belegung", $"/verwaltung/objekte/{p}/versorgung", $"/verwaltung/objekte/{p}/umlageschluessel", $"/verwaltung/objekte/{p}/heizung", $"/verwaltung/objekte/{p}/nebenkosten?jahr={year}",
            $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=1", $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=2", $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=3", $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=4", $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=5", $"/verwaltung/objekte/{p}/abrechnung?jahr={year}&schritt=6",
            "/verwaltung/benutzer", $"/verwaltung/benutzer/{Ids["user"]}",
            "/verwaltung/einstellungen/vermieter", "/verwaltung/einstellungen/vorlagen", "/verwaltung/einstellungen/vorlagen/neu", "/verwaltung/dokumente/schreiben", "/verwaltung/system/mails", "/verwaltung/system/jobs", "/verwaltung/system/datensicherung", "/verwaltung/system/belegauslesung",
            "/konto/sicherheit",
        ];
    }

    // The long page sweeps are split into parts of the route list that live in separate classes (see PageSweepTests.cs), so that they
    // run in parallel: tests of one class run one after the other, and a single sweep took minutes and held up the whole run.
    public const int Parts = 6;

    private IEnumerable<string> Part(int part) => Routes().Concat(OptionalRoutes()).Where((_, index) => index % Parts == part);

    protected IEnumerable<string> OptionalRoutes()
    {
        if (Ids.TryGetValue("statement", out var statement))
        {
            yield return $"/verwaltung/abrechnungen/{statement}";
        }

        if (Ids.TryGetValue("dunning", out var dunning))
        {
            yield return $"/verwaltung/finanzen/mahnungen/{dunning}";
        }

        if (Ids["import"].Length > 0)
        {
            yield return $"/verwaltung/bank/importe/{Ids["import"]}";
        }
    }

    protected async Task EveryPageLoadsAsync(bool mobileView, int part)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        var problems = new List<string>();
        var current = "";
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                problems.Add($"{current}: Konsole: {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"{current}: Skriptfehler: {error}");
        page.RequestFailed += (_, request) =>
        {
            // Chromium does not show PDFs inline in an automated run and aborts those requests.
            if (!request.Url.Contains("/_blazor", StringComparison.Ordinal) && !request.Url.Contains(".pdf", StringComparison.Ordinal))
            {
                problems.Add($"{current}: Anfrage fehlgeschlagen: {request.Url} {request.Failure}");
            }
        };

        foreach (var mobile in new[] { mobileView })
        {
            await page.SetViewportSizeAsync(mobile ? 360 : 1280, mobile ? 780 : 900);
            foreach (var path in Part(part))
            {
                current = (mobile ? "[360] " : "[Desktop] ") + path;
                var response = await page.GotoAsync(BaseUrl + path);
                if (response?.Status != 200)
                {
                    problems.Add($"{current}: Status {response?.Status}");
                    continue;
                }

                // Interactive pages: wait for the circuit; then look at the final state.
                var interactive = await page.Locator("[data-interactive=true], .mud-main-content, main").First.CountAsync() > 0;
                try
                {
                    await page.Locator("[data-interactive=true]").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 3000 });
                }
                catch (TimeoutException)
                {
                    interactive = false; // static page
                }

                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                var body = await page.InnerTextAsync("body");

                // Under load (the whole suite runs in parallel) a page may take a moment longer to leave its loading state; a real hang still fails.
                for (var waited = 0; waited < 15000 && body.Contains("Wird geladen …", StringComparison.Ordinal); waited += 250)
                {
                    await page.WaitForTimeoutAsync(250);
                    body = await page.InnerTextAsync("body");
                }

                if (body.Contains("Es ist ein Fehler aufgetreten", StringComparison.Ordinal) || body.Contains("Wird geladen …", StringComparison.Ordinal))
                {
                    problems.Add($"{current}: Fehlerseite oder hängt im Ladezustand{(interactive ? "" : " (statisch)")}");
                }

                if (await page.Locator("main h1").CountAsync() == 0)
                {
                    problems.Add($"{current}: keine Überschrift");
                }

                var width = await page.EvaluateAsync<int[]>("() => { const c = document.getElementById('content'); return [Math.max(document.documentElement.scrollWidth, c ? c.scrollWidth : 0), window.innerWidth]; }");
                if (width[0] > width[1])
                {
                    var culprits = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('#content *')].filter(e => e.getBoundingClientRect().right > window.innerWidth + 1 && !(w => w && ['auto', 'scroll'].includes(getComputedStyle(w).overflowX))(e.closest('.table-wrapper,.mud-table-container'))).slice(0, 3).map(e => e.tagName.toLowerCase() + (typeof e.className === 'string' && e.className ? '.' + e.className.split(' ').join('.') : '') + ' ' + Math.round(e.getBoundingClientRect().right))");
                    problems.Add($"{current}: {width[0]}px breit bei {width[1]}px; {string.Join(", ", culprits)}");
                }

                if (width[1] <= 400)
                {
                    problems.AddRange((await page.EvaluateAsync<string[]>(UiAuditTests.ControlsScript)).Select(f => $"{current}: {f}"));
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    protected async Task PagesMeetContrastAsync(bool dark, int part)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        var problems = new List<string>();
        foreach (var scheme in new[] { dark ? ColorScheme.Dark : ColorScheme.Light })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            foreach (var path in Part(part))
            {
                await page.GotoAsync(BaseUrl + path);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // A page is first prerendered and then becomes interactive; measured in between, the dark scheme can still show
                // light surfaces. A real contrast problem stays, so measure again for a moment before reporting.
                var found = await page.EvaluateAsync<string[]>(UiAuditTests.ContrastScript);
                for (var attempt = 0; attempt < 8 && found.Length > 0; attempt++)
                {
                    await page.WaitForTimeoutAsync(250);
                    found = await page.EvaluateAsync<string[]>(UiAuditTests.ContrastScript);
                }

                problems.AddRange(found.Select(f => $"[{scheme}] {path}: {f}"));
            }
        }

        Assert.True(problems.Count == 0, "Kontrast unter WCAG AA:\n" + string.Join("\n", problems.Distinct()));
    }
}

/// <summary>The dialogs behind the add buttons and the on-demand screenshot tour; the page sweeps live in <see cref="PageSweepBase"/>.</summary>
public class PageSmokeTests(PostgresFixture postgres) : PageSweepBase(postgres)
{
    /// <summary>Screenshots of every page with data (viewport only), on demand: ELOCARO_TOUR=1 ELOCARO_TOUR_DIR=/pfad; ELOCARO_TOUR_ONLY=personen,bank/regeln limits it to paths containing one of the parts.</summary>
    [Fact]
    public async Task Screenshot_tour_of_all_pages()
    {
        if (Environment.GetEnvironmentVariable("ELOCARO_TOUR") is null)
        {
            Assert.Skip("Tour nur auf Abruf: ELOCARO_TOUR=1");
        }

        var directory = Path.Combine(Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") ?? Path.Combine(Path.GetTempPath(), "elocaro-tour"), "alle");
        Directory.CreateDirectory(directory);
        var page = await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        if (Environment.GetEnvironmentVariable("ELOCARO_TOUR_DARK") is not null)
        {
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        }

        var only = Environment.GetEnvironmentVariable("ELOCARO_TOUR_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var mobile in new[] { false, true })
        {
            await page.SetViewportSizeAsync(mobile ? 360 : 1280, Environment.GetEnvironmentVariable("ELOCARO_TOUR_FULL") is not null ? 2600 : mobile ? 780 : 900);
            var index = 0;
            foreach (var path in Routes().Concat(OptionalRoutes()))
            {
                if (only is not null && !only.Any(o => path.Contains(o, StringComparison.Ordinal)))
                {
                    index++;
                    continue;
                }

                await page.GotoAsync(BaseUrl + path);
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
                }
                catch (TimeoutException)
                {
                    // Pages with a permanent connection never become idle; the screenshot is taken anyway.
                }

                await page.WaitForTimeoutAsync(300);
                var height = await page.EvaluateAsync<int>("() => { const c = document.querySelector('main.content') ?? document.querySelector('main'); return c ? c.scrollHeight : 0; }");
                var name = path.Replace(Ids["property"], "P", StringComparison.Ordinal).Trim('/').Replace("verwaltung/", "", StringComparison.Ordinal);
                name = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{(mobile ? "m" : "d")}{index++:00}-{name[..Math.Min(name.Length, 60)]}.png"), FullPage = Environment.GetEnvironmentVariable("ELOCARO_TOUR_FULL") is not null });
                await File.AppendAllTextAsync(Path.Combine(directory, "heights.txt"), $"{(mobile ? "m" : "d")} {height,6}px {path}\n", TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>Opens the dialogs behind the "add" buttons of the detail pages: each must show a dialog without script errors.</summary>
    [Fact]
    public async Task Add_buttons_on_detail_pages_open_dialogs_without_errors()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        var problems = new List<string>();
        var current = "";
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                problems.Add($"{current}: Konsole: {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"{current}: Skriptfehler: {error}");

        string[] paths =
        [
            $"/verwaltung/personen/{Ids["person"]}", $"/verwaltung/objekte/{Ids["property"]}", $"/verwaltung/einheiten/{Ids["unit"]}", $"/verwaltung/zimmer/{Ids["room"]}",
            $"/verwaltung/vertraege/{Ids["lease"]}", $"/verwaltung/objekte/{Ids["property"]}/zaehler", $"/verwaltung/zaehler/{Ids["meter"]}", "/verwaltung/personen", "/verwaltung/bank/konten",
            $"/verwaltung/vertraege/{Ids["lease"]}/konto", "/verwaltung/finanzen/mahnungen",
        ];
        var opened = 0;
        var mobileDialogs = Environment.GetEnvironmentVariable("ELOCARO_TOUR_MOBILE") is not null;
        if (mobileDialogs)
        {
            await page.SetViewportSizeAsync(360, 780);
        }

        foreach (var path in paths)
        {
            current = path;
            await GotoInteractiveAsync(page, path);
            var tabs = page.GetByRole(AriaRole.Tab);
            var tabCount = await tabs.CountAsync();
            for (var tab = -1; tab < tabCount; tab++)
            {
                if (tab >= 0)
                {
                    await tabs.Nth(tab).ClickAsync();
                    await page.WaitForTimeoutAsync(600); // the old panel is still visible while the tab slides
                }

                var buttons = page.Locator("main button.mud-button-root:not([type=submit])").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("anlegen|hinzufügen|erfassen|Neu|Ändern", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).Filter(new() { Visible = true });
                var count = await buttons.CountAsync();
                for (var i = 0; i < count; i++)
                {
                    var button = buttons.Nth(i);
                    var label = (await button.InnerTextAsync()).Trim();
                    await button.ClickAsync();
                    try
                    {
                        await page.Locator(".mud-dialog").First.WaitForAsync(new() { Timeout = 4000 });
                    }
                    catch (TimeoutException)
                    {
                        problems.Add($"{path}: „{label}“ öffnet keinen Dialog");
                        await GotoInteractiveAsync(page, path);
                        continue;
                    }

                    opened++;
                    await page.WaitForTimeoutAsync(400);
                    var width = await page.EvaluateAsync<int[]>("() => { const d = document.querySelector('.mud-dialog').getBoundingClientRect(); return [Math.round(d.right), window.innerWidth, Math.round(d.bottom), window.innerHeight]; }");
                    if (width[0] > width[1] || width[2] > width[3])
                    {
                        problems.Add($"{path}: Dialog „{label}“ ragt aus dem Fenster ({width[0]}/{width[1]}, {width[2]}/{width[3]})");
                    }

                    if (Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") is { } tourDirectory)
                    {
                        var folder = Path.Combine(tourDirectory, "dialoge");
                        Directory.CreateDirectory(folder);
                        var file = new string($"{(mobileDialogs ? "m" : "d")}{opened:00}-{label}".Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
                        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, file + ".png") });
                    }

                    if (mobileDialogs)
                    {
                        problems.AddRange((await page.EvaluateAsync<string[]>(UiAuditTests.ControlsScript)).Where(f => !f.Contains("mud-input-clear", StringComparison.Ordinal)).Select(f => $"{path}: Dialog „{label}“: {f}"));
                    }

                    await page.Keyboard.PressAsync("Escape");
                    await page.Locator(".mud-dialog").First.WaitForAsync(new() { State = WaitForSelectorState.Detached });
                }
            }
        }

        Assert.True(opened > 0, "Es wurde kein Dialog geöffnet.");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }
}
