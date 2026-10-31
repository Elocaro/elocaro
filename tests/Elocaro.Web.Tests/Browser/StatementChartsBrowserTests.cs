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
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// The charts and the derivation cell of the tenant's statement page, rendered for real at phone and desktop width in light and dark.
/// With <c>ELOCARO_TOUR_DIR</c> set it also saves screenshots to look at.
/// </summary>
public class StatementChartsBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string Email = "mieterin@example.de";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(3, "nachzahlung")]
    [InlineData(10, "guthaben")]
    public async Task Statement_page_fits_every_width_and_theme_and_reads_the_derivation_as_a_block(int paidMonths, string name)
    {
        var dispatch = await SetUpAsync(paidMonths);
        await SignedInAsAdminAsync(); // makes sure the app is set up
        var page = await NewPageAsync();
        await page.GotoAsync(BaseUrl + "/konto/anmelden");
        await page.FillAsync("#email", Email);
        await page.FillAsync("#password", Password);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/konto/", StringComparison.Ordinal));

        var directory = Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR");
        var problems = new List<string>();
        foreach (var width in new[] { 360, 1280 })
        {
            await page.SetViewportSizeAsync(width, 900);
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await page.EmulateMediaAsync(new() { ColorScheme = scheme });
                await page.GotoAsync($"{BaseUrl}/portal/abrechnungen/{dispatch}");
                await Assertions.Expect(page.Locator("#comparison-chart")).ToBeVisibleAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);

                    // Only the content area scrolls, so the window has to be as tall as the content for the whole page to be on the picture.
                    var height = await page.EvaluateAsync<int>("() => { const c = document.getElementById('content'); return c ? c.scrollHeight + Math.round(c.getBoundingClientRect().top) : document.documentElement.scrollHeight; }");
                    await page.SetViewportSizeAsync(width, Math.Max(900, height));
                    await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{width}-{scheme}.png"), FullPage = true });
                    await page.SetViewportSizeAsync(width, 900);
                }

                await NoHorizontalScrollAsync(page);
                await Assertions.Expect(page.Locator("#result-chart")).ToContainTextAsync(paidMonths == 3 ? "Nachzahlung" : "Guthaben");
                problems.AddRange((await page.EvaluateAsync<string[]>(UiAuditTests.ContrastScript)).Select(f => $"[{width}/{scheme}] {f}"));
            }

            if (width == 360)
            {
                await ExpectBlockAtPhoneWidthAsync(page);
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static async Task ExpectBlockAtPhoneWidthAsync(IPage page)
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
