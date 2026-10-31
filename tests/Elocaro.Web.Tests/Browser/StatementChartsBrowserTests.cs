using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Files;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.OperatingCosts;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Application.Receipts;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.People;
using Elocaro.Domain.Statements;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// The charts and the derivation cell of the tenant's statement page, rendered for real from released statements.
/// With <c>ELOCARO_TOUR_DIR</c> set it also saves screenshots to look at.
/// </summary>
public class StatementChartsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Email = "mieterin@example.de";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(3, "nachzahlung", "Nachzahlung")]
    [InlineData(10, "guthaben", "Guthaben")]
    public async Task Statement_page_fits_every_width_and_theme_and_reads_the_derivation_as_a_block(int paidMonths, string name, string result)
    {
        // Every month of the billing year has its charge, whenever the test runs.
        var end = new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero);
        if (App.Clock.GetUtcNow() < end)
        {
            App.Clock.SetUtcNow(end);
        }

        var dispatch = await SetUpAsync(paidMonths);
        var page = await NewPageAsync();
        await StatementPageChecks.SignInAsync(page, BaseUrl, Email, Password);

        var problems = new List<string>();
        foreach (var width in new[] { 360, 1280 })
        {
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await StatementPageChecks.OpenAsync(page, $"{BaseUrl}/portal/abrechnungen/{dispatch}", width, scheme);
                await StatementPageChecks.SnapAsync(page, $"{name}-{width}-{scheme}", width);
                await NoHorizontalScrollAsync(page);
                await Assertions.Expect(page.Locator("#statement-result")).ToContainTextAsync(result);
                await Assertions.Expect(page.Locator("#result-chart")).ToContainTextAsync(result);
                await Assertions.Expect(page.Locator("#comparison-chart")).ToBeVisibleAsync();
                problems.AddRange(await StatementPageChecks.ProblemsAsync(page, $"{width}/{scheme}"));
            }

            if (width == 360)
            {
                await ExpectPhoneLayoutAsync(page);
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static async Task ExpectPhoneLayoutAsync(IPage page)
    {
        // The derivation fills the card and runs from the left, under its label.
        var cell = await page.Locator("table.statement-lines tbody td.derivation").First.EvaluateAsync<string[]>(
            "e => { const s = getComputedStyle(e); return [s.display, s.textAlign, String(Math.round(e.getBoundingClientRect().width)), String(Math.round(e.closest('tr').clientWidth))]; }");
        Assert.Equal("block", cell[0]);
        Assert.Equal("left", cell[1]);
        Assert.True(int.Parse(cell[2], System.Globalization.CultureInfo.InvariantCulture) >= int.Parse(cell[3], System.Globalization.CultureInfo.InvariantCulture) - 32, $"Rechenweg {cell[2]}px breit in einer Karte mit {cell[3]}px.");

        // Other card tables keep label and amount side by side.
        var other = await page.Locator("#balances tbody td[data-label]").First.EvaluateAsync<string>("e => getComputedStyle(e).display");
        Assert.Equal("flex", other);

        // Each legend entry has its own line.
        var tops = await page.Locator(".chart-legend li").EvaluateAllAsync<double[]>("items => items.map(i => i.getBoundingClientRect().top)");
        Assert.Equal(tops.Length, tops.Distinct().Count());
    }

    /// <summary>A tenant with a statement of 2026 and a previous one of 2025 (shorter tenancy, other cost types).</summary>
    private async Task<Guid> SetUpAsync(int paidMonths) => await App.WithScopeAsync(async sp =>
    {
        var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Email, Email = Email, EmailConfirmed = true, DisplayName = "Anna Schmidt" };
        Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
        await manager.AddToRoleAsync(user, Roles.Tenant);

        var properties = sp.GetRequiredService<IPropertyService>();
        var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
        var unit = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
        var other = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 2" }, Ct)).Id!.Value;
        await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 60m }, Ct);
        await properties.SetUnitAreaAsync(other, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 40m }, Ct);
        var anna = (await sp.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct)).Id!.Value;
        var lease = (await sp.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2025, 3, 1), TenantIds = [anna], BaseRent = 500m, OperatingCostPrepayment = 100m }, Ct)).Id!.Value;
        var db = sp.GetRequiredService<ElocaroDbContext>();
        (await db.Set<Person>().SingleAsync(p => p.Id == anna, Ct)).UserId = user.Id;
        await db.SaveChangesAsync(Ct);

        await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 1, 1) }, Ct);
        var accounts = sp.GetRequiredService<ITenantAccounts>();
        await accounts.GenerateChargesAsync(null, Ct);
        for (var month = 1; month <= paidMonths; month++)
        {
            Assert.True((await accounts.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, month, 5), Amount = 600m }, Ct)).Succeeded);
        }

        await CostsAsync(sp, house, 2025, "betrkv-01", 900m);
        await CostsAsync(sp, house, 2025, "betrkv-10", 200m);
        await CostsAsync(sp, house, 2025, "betrkv-13", 500m);
        await CostsAsync(sp, house, 2026, "betrkv-01", 800m); // 966.00 € share in all: above 3 months of prepayments, below 10
        await CostsAsync(sp, house, 2026, "betrkv-13", 450m);
        await CostsAsync(sp, house, 2026, "betrkv-09", 300m);
        await CostsAsync(sp, house, 2026, "betrkv-11", 60m);

        var files = sp.GetRequiredService<IFileStore>();
        using var letter = new MemoryStream("%PDF-1.4\n%%EOF\n"u8.ToArray());
        var file = await files.SaveAsync(letter, "abrechnung.pdf", Ct);
        var statements = sp.GetRequiredService<IStatements>();
        var previous = await statements.ReleaseAsync(house, Period.CalendarYear(2025), true, null, Ct);
        Assert.True(previous.Succeeded, string.Join(", ", previous.Errors));
        var current = await statements.ReleaseAsync(house, Period.CalendarYear(2026), true, null, Ct);
        Assert.True(current.Succeeded, string.Join(", ", current.Errors));
        db.Add(new StatementDispatch(previous.Id!.Value, lease, DispatchChannel.Post, new DateOnly(2026, 2, 1), file.Id, null, null, 0m, 0m, null, null));
        var dispatch = new StatementDispatch(current.Id!.Value, lease, DispatchChannel.Post, new DateOnly(2027, 2, 1), file.Id, null, null, 0m, 0m, null, null);
        db.Add(dispatch);
        await db.SaveChangesAsync(Ct);
        return dispatch.Id;
    });

    private static async Task CostsAsync(IServiceProvider sp, Guid house, int year, string costType, decimal amount)
    {
        Assert.True((await sp.GetRequiredService<ICostAllocation>().SetRuleAsync(house, new CostRuleInput { CostType = costType, From = new DateOnly(2020, 1, 1), Key = AllocationKey.LivingArea }, Ct)).Succeeded);
        var receipt = await sp.GetRequiredService<IReceiptService>().CreateAsync(
            new ReceiptInput { SupplierName = "Versorger", InvoiceNumber = $"{costType}-{year}", InvoiceDate = new DateOnly(year, 12, 15), Gross = amount },
            [],
            new CostItemInput { CostType = costType, PropertyId = house, From = new DateOnly(year, 1, 1), To = new DateOnly(year, 12, 31) },
            Ct);
        Assert.True(receipt.Succeeded, string.Join(", ", receipt.Errors));
    }
}

/// <summary>
/// Values a released statement cannot easily produce (very large amounts, long names, a previous value of exactly 0), served by a
/// stand-in for the statement service: narrow widths, print and forced colours.
/// </summary>
public class StatementChartsEdgeCaseBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Email = "mieterin@example.de";
    private const string LongName = "Sonstige Betriebskosten: Wartung der Rauchwarnmelder, Dachrinnenreinigung und Prüfung der Rückstausicherung";

    private static readonly Guid Extreme = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Balanced = Guid.Parse("00000000-0000-0000-0000-000000000002");

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Scoped<ITenantStatements, FixedStatements>());

    [Fact]
    public async Task Extreme_values_fit_down_to_320_pixels_and_keep_their_text_in_print_and_forced_colours()
    {
        var page = await SignedInAsync();
        var url = $"{BaseUrl}/portal/abrechnungen/{Extreme}";
        var problems = new List<string>();
        foreach (var width in new[] { 320, 360, 1280 })
        {
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await StatementPageChecks.OpenAsync(page, url, width, scheme);
                await StatementPageChecks.SnapIfAsync(page, $"extrem-{width}-{scheme}", width, width != 360 && (width == 320 || scheme == ColorScheme.Light));
                await NoHorizontalScrollAsync(page);
                problems.AddRange(await StatementPageChecks.ProblemsAsync(page, $"{width}/{scheme}"));
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        await Assertions.Expect(page.Locator("#comparison-chart")).ToContainTextAsync("+123.456,78 €"); // previous value 0: a normal difference
        await Assertions.Expect(page.Locator("#comparison-chart [data-cost-type=zero] .cost-chart-prev")).ToHaveCountAsync(1);

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light, Media = Media.Print });
        await page.SetViewportSizeAsync(1280, 900);
        await StatementPageChecks.SnapIfAsync(page, "extrem-print", 1280, true);
        await ExpectAmountsReadableAsync(page);

        await page.EmulateMediaAsync(new() { Media = Media.Screen, ForcedColors = ForcedColors.Active });
        await page.SetViewportSizeAsync(360, 900);
        await StatementPageChecks.SnapIfAsync(page, "extrem-forced-colors", 360, true);
        await ExpectAmountsReadableAsync(page);
        var bar = await page.Locator("#result-chart .cost-chart-bar").First.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", bar);
    }

    [Fact]
    public async Task Balanced_result_without_a_previous_statement_says_so_in_words()
    {
        var page = await SignedInAsync();
        foreach (var width in new[] { 320, 1280 })
        {
            await StatementPageChecks.OpenAsync(page, $"{BaseUrl}/portal/abrechnungen/{Balanced}", width, ColorScheme.Light);
            await StatementPageChecks.SnapIfAsync(page, $"ausgeglichen-{width}-Light", width, true);
            await NoHorizontalScrollAsync(page);
            await Assertions.Expect(page.Locator("#statement-result")).ToContainTextAsync("Ausgeglichen");
            await Assertions.Expect(page.Locator("#result-chart [data-row=result]")).ToContainTextAsync("Ausgeglichen");
            await Assertions.Expect(page.Locator("#balances")).ToContainTextAsync("0,00 € Nachzahlung"); // the table stays as it is
            await Assertions.Expect(page.Locator("#comparison-chart")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#previous-year")).ToContainTextAsync("keine an Sie versendete Abrechnung");
        }
    }

    private static async Task ExpectAmountsReadableAsync(IPage page)
    {
        var values = page.Locator(".statement-chart .cost-chart-value");
        await Assertions.Expect(values.First).ToBeVisibleAsync();
        var texts = await values.EvaluateAllAsync<string[]>("items => items.filter(i => i.getBoundingClientRect().width > 0 && getComputedStyle(i).visibility === 'visible').map(i => i.textContent)");
        Assert.Contains("123.456,78 €", texts);
        Assert.Contains("+123.456,78 €", texts);
    }

    private async Task<IPage> SignedInAsync()
    {
        await App.WithScopeAsync(async sp =>
        {
            var manager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = Email, Email = Email, EmailConfirmed = true, DisplayName = "Anna Schmidt" };
            Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
            await manager.AddToRoleAsync(user, Roles.Tenant);
            return true;
        });
        var page = await NewPageAsync();
        await StatementPageChecks.SignInAsync(page, BaseUrl, Email, Password);
        return page;
    }

    private sealed class FixedStatements : ITenantStatements
    {
        public Task<IReadOnlyList<PortalStatementRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PortalStatementRow>>([]);

        public Task<PortalStatement?> GetAsync(ClaimsPrincipal user, Guid dispatchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PortalStatement?>(dispatchId == Extreme ? ExtremeStatement() : BalancedStatement());

        public Task<OpenedFile?> OpenReceiptFileAsync(ClaimsPrincipal user, Guid dispatchId, Guid receiptId, Guid fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OpenedFile?>(null);

        private static PortalStatement ExtremeStatement() => new(
            Header(123_456.78m + 98_765.43m + 12.34m - 1_000m),
            Period.CalendarYear(2026),
            365,
            null,
            [Line("zero", LongName, 123_456.78m), Line("large", "Laufende öffentliche Lasten (Grundsteuer)", 98_765.43m), Line("tiny", "Aufzug", 12.34m)],
            [new PortalBalance("Betriebskosten", 222_234.55m, 1_000m, 221_234.55m), new PortalBalance("Heizkosten", 0m, 0m, 0m)],
            221_234.55m,
            new YearComparison(
                Period.CalendarYear(2025),
                365,
                365,
                [
                    new LineComparison("zero", LongName, 123_456.78m, 0m),
                    new LineComparison("large", "Laufende öffentliche Lasten (Grundsteuer)", 98_765.43m, 123_456.78m),
                    new LineComparison("tiny", "Aufzug", 12.34m, null),
                    new LineComparison("gone", "Sach- und Haftpflichtversicherung", null, 100_000.60m),
                ],
                223_457.38m,
                222_234.55m),
            []);

        private static PortalStatement BalancedStatement() => new(
            Header(0m),
            Period.CalendarYear(2026),
            365,
            null,
            [Line("a", "Laufende öffentliche Lasten (Grundsteuer)", 600m), Line("b", "Aufzug", 200m)],
            [new PortalBalance("Betriebskosten", 800m, 800m, 0m), new PortalBalance("Heizkosten", 0m, 0m, 0m)],
            0m,
            null,
            []);

        private static PortalStatementRow Header(decimal balance) =>
            new(Extreme, Guid.Empty, Guid.Empty, "Wohnung 1", "Lindenweg 3", Period.CalendarYear(2026), 1, new DateOnly(2027, 2, 1), balance, false);

        private static PortalStatementLine Line(string costType, string name, decimal share) =>
            new(costType, name, false, "Wohnfläche", share * 2, share, $"{share * 2:0.00} € nach Wohnfläche: Ihr Anteil {share:0.00} €", ["Ihr Anteil am Schlüssel: 60 von 100 m² (Ø 365 Tage) = 60,00 %"]);
    }
}

/// <summary>Steps shared by the browser tests of the statement page.</summary>
internal static class StatementPageChecks
{
    public static async Task SignInAsync(IPage page, string baseUrl, string email, string password)
    {
        await page.GotoAsync(baseUrl + "/konto/anmelden");
        await page.FillAsync("#email", email);
        await page.FillAsync("#password", password);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/konto/", StringComparison.Ordinal));
    }

    public static async Task OpenAsync(IPage page, string url, int width, ColorScheme scheme)
    {
        await page.SetViewportSizeAsync(width, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = scheme });
        await page.GotoAsync(url);
        await Assertions.Expect(page.Locator("#result-chart")).ToBeVisibleAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Text below AA contrast, and markers that leave their track.</summary>
    public static async Task<IEnumerable<string>> ProblemsAsync(IPage page, string where)
    {
        var contrast = await page.EvaluateAsync<string[]>(UiAuditTests.ContrastScript);
        var overhang = await page.Locator(".statement-chart .cost-chart-prev").EvaluateAllAsync<int>(
            "items => items.filter(m => { const a = m.getBoundingClientRect(), t = m.parentElement.getBoundingClientRect(); return a.left < t.left - 0.01 || a.right > t.right + 0.01; }).length");
        return contrast.Concat(overhang > 0 ? [$"{overhang} Striche stehen über"] : []).Select(p => $"[{where}] {p}");
    }

    public static Task SnapAsync(IPage page, string name, int width) => SnapIfAsync(page, name, width, true);

    /// <summary>A picture of the whole page when <c>ELOCARO_TOUR_DIR</c> is set.</summary>
    public static async Task SnapIfAsync(IPage page, string name, int width, bool wanted)
    {
        if (!wanted || Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);

        // Only the content area scrolls, so the window has to be as tall as the content for the whole page to be on the picture.
        var height = await page.EvaluateAsync<int>("() => { const c = document.getElementById('content'); return c ? c.scrollHeight + Math.round(c.getBoundingClientRect().top) : document.documentElement.scrollHeight; }");
        await page.SetViewportSizeAsync(width, Math.Max(900, height));
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png"), FullPage = true });
        await page.SetViewportSizeAsync(width, 900);
    }
}
