using System.Globalization;
using Elocaro.Application.Documents;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>Screenshots for README and website with a realistic portfolio (two houses, flats, a shared flat), on demand: ELOCARO_SHOWCASE=1 ELOCARO_TOUR_DIR=/pfad.</summary>
public class ShowcaseTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped<IPdfGenerator, FakePdfGenerator>();

    [Fact]
    public async Task Screenshots_of_a_realistic_portfolio()
    {
        if (Environment.GetEnvironmentVariable("ELOCARO_SHOWCASE") is null)
        {
            Assert.Skip("Nur auf Abruf: ELOCARO_SHOWCASE=1");
        }

        var directory = Environment.GetEnvironmentVariable("ELOCARO_TOUR_DIR") ?? Path.Combine(Path.GetTempPath(), "elocaro-showcase");
        Directory.CreateDirectory(directory);
        var page = await SignedInAsAdminAsync(mobile: false);
        var (house, lease) = await SeedPortfolioAsync();

        await page.SetViewportSizeAsync(1280, 900);
        await ShootAsync(page, "/verwaltung", Path.Combine(directory, "dashboard.png"));
        await ShootAsync(page, "/verwaltung/objekte", Path.Combine(directory, "objekte.png"));
        await ShootAsync(page, $"/verwaltung/objekte/{house}", Path.Combine(directory, "objekt.png"));
        await ShootAsync(page, $"/verwaltung/vertraege/{lease}", Path.Combine(directory, "mietvertrag.png"));
        await page.SetViewportSizeAsync(1280, 1000);
        await ShootAsync(page, $"/verwaltung/objekte/{house}/belegung", Path.Combine(directory, "belegung.png"));

        await page.SetViewportSizeAsync(360, 780);
        await ShootAsync(page, "/verwaltung", Path.Combine(directory, "handy-aufgaben.png"));
        await ShootAsync(page, $"/verwaltung/objekte/{house}/belegung", Path.Combine(directory, "handy-belegung.png"));
    }

    private async Task ShootAsync(IPage page, string path, string target)
    {
        await page.GotoAsync(BaseUrl + path);
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            // Pages with a permanent connection never become idle; the screenshot is taken anyway.
        }

        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new() { Path = target });
    }

    private async Task<(Guid House, Guid Lease)> SeedPortfolioAsync()
    {
        var today = GermanTime.Today(App.Clock.GetUtcNow());
        var start = new DateOnly(today.Year - 1, 1, 1);
        return await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var people = sp.GetRequiredService<IPersonService>();
            var leases = sp.GetRequiredService<ILeaseService>();
            var accounts = sp.GetRequiredService<ITenantAccounts>();
            await sp.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = start }, Ct);

            var leaseIds = new List<(Guid Id, decimal Rent, Guid From)>();
            Guid first = default;

            async Task<Guid> HouseAsync(string name, string street, string postalCode)
            {
                return (await properties.CreateAsync(new PropertyInput { Name = name, Street = street, PostalCode = postalCode, City = "Köln" }, Ct)).Id!.Value;
            }

            async Task<Guid> FlatAsync(Guid property, string name, string location, decimal area)
            {
                var unit = (await properties.CreateUnitAsync(property, new UnitInput { Name = name, Location = location }, Ct)).Id!.Value;
                await properties.SetUnitAreaAsync(unit, new AreaInput { From = start, Area = area }, Ct);
                return unit;
            }

            async Task<Guid> TenantAsync(string firstName, string lastName) =>
                (await people.CreateAsync(new PersonInput { FirstName = firstName, LastName = lastName, Email = $"{firstName.ToLowerInvariant()}@example.de" }, null, Ct)).Id!.Value;

            async Task RentAsync(Guid rentalUnit, Guid tenant, decimal baseRent, decimal operatingCosts, decimal heatingCosts)
            {
                var id = (await leases.CreateAsync(
                    new LeaseCreation { RentalUnitId = rentalUnit, Start = start, TenantIds = [tenant], BaseRent = baseRent, OperatingCostPrepayment = operatingCosts, HeatingCostPrepayment = heatingCosts }, Ct)).Id!.Value;
                leaseIds.Add((id, baseRent + operatingCosts + heatingCosts, rentalUnit));
                if (first == default)
                {
                    first = id;
                }
            }

            var main = await HouseAsync("Lindenweg 3", "Lindenweg 3", "50667");
            await RentAsync(await FlatAsync(main, "Wohnung 1", "EG links", 62m), await TenantAsync("Anna", "Schmidt"), 650m, 120m, 40m);
            await RentAsync(await FlatAsync(main, "Wohnung 2", "EG rechts", 58m), await TenantAsync("Ben", "Weber"), 620m, 115m, 40m);
            await RentAsync(await FlatAsync(main, "Wohnung 3", "1. OG", 74m), await TenantAsync("Clara", "Hoffmann"), 780m, 140m, 50m);
            await FlatAsync(main, "Wohnung 4", "DG", 49m);

            var shared = await FlatAsync(main, "WG Dachgarten", "2. OG", 96m);
            string[] roomTenants = ["Dana Krüger", "Emil Wagner"];
            for (var i = 0; i < 3; i++)
            {
                var room = (await properties.CreateRoomAsync(shared, new RoomInput { Name = $"Zimmer {i + 1}" }, Ct)).Id!.Value;
                if (i >= roomTenants.Length)
                {
                    continue; // the third room stays empty
                }

                var name = roomTenants[i].Split(' ');
                var id = (await leases.CreateAsync(
                    new LeaseCreation { RentalUnitId = room, Start = start, TenantIds = [await TenantAsync(name[0], name[1])], BillingType = BillingType.FlatRate, BaseRent = 390m, OperatingCostFlatRate = 90m }, Ct)).Id!.Value;
                leaseIds.Add((id, 480m, room));
            }

            var garden = await HouseAsync("Gartenstraße 12", "Gartenstraße 12", "50823");
            await RentAsync(await FlatAsync(garden, "Erdgeschoss", "EG", 70m), await TenantAsync("Fiona", "Neumann"), 700m, 130m, 45m);
            await RentAsync(await FlatAsync(garden, "Obergeschoss", "1. OG", 70m), await TenantAsync("Georg", "Bauer"), 720m, 130m, 45m);

            await accounts.GenerateChargesAsync(null, Ct);

            // Everybody pays on time, except the third lease, which is one month behind: a small arrears item for the dashboard.
            var months = Enumerable.Range(0, 24).Select(i => start.AddMonths(i)).Where(m => m <= new DateOnly(today.Year, today.Month, 1)).ToList();
            foreach (var (id, rent, _) in leaseIds)
            {
                var behind = leaseIds[2].Id == id;
                foreach (var month in months)
                {
                    var date = month.AddDays(2);
                    if (date > today || (behind && month == months[^1]))
                    {
                        continue;
                    }

                    await accounts.RecordPaymentAsync(id, new PaymentInput { Date = date, Amount = rent, Purpose = "Miete " + month.ToString("MM/yyyy", CultureInfo.InvariantCulture) }, Ct);
                }
            }

            return (main, first);
        });
    }
}
