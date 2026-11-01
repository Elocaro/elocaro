using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Metering;
using Elocaro.Domain.Metering;
using Elocaro.Web.Tests.Account;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Web.Tests.Portal;

/// <summary>The consumption chart of the tenant portal: bars and markers agree with the figures, the weather-adjusted change is text with an arrow.</summary>
public class ConsumptionChartTests(PostgresFixture postgres) : AccountTestBase(postgres)
{
    protected override void ConfigureServices(IServiceCollection services) => services.AddScoped<IConsumptionInformation, FakeInformation>();

    [Fact]
    public async Task Chart_shows_bars_against_the_same_month_of_the_year_before_and_the_weather_adjusted_change()
    {
        var (tenant, _) = await SignedInAsAsync(Roles.Tenant);
        using var _ = tenant;

        var page = await tenant.GetAsync("/portal/verbrauch", followRedirects: false);

        var chart = page.Document.QuerySelector("figure.consumption-chart[data-meter-type=Heat]")!;
        var columns = chart.QuerySelectorAll(".consumption-column").ToList();
        Assert.Equal(["Jan", "Feb"], columns.Select(c => c.QuerySelector(".consumption-month")!.TextContent.Trim()));
        Assert.Equal("height:50%", columns[0].QuerySelector(".consumption-bar")!.GetAttribute("style")); // 500 of 1,000 kWh
        Assert.Equal("--at:100%", columns[0].QuerySelector(".consumption-prev")!.GetAttribute("style"));
        Assert.Null(columns[1].QuerySelector(".consumption-bar")); // no value, no bar
        Assert.Contains("▲ +25", columns[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("▼ −10", columns[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Januar 2026 500, Vorjahresmonat 1.000", chart.QuerySelector("[role=img]")!.GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    private sealed class FakeInformation : IConsumptionInformation
    {
        public Task<IReadOnlyList<MonthlyConsumption>> ForLeaseAsync(Guid leaseId, int months, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MonthlyConsumption>>([]);

        public Task<MonthlyConsumption?> ForLeaseMonthAsync(Guid leaseId, DateOnly month, CancellationToken cancellationToken = default) =>
            Task.FromResult<MonthlyConsumption?>(null);

        public Task<IReadOnlyList<TenantConsumption>> ForTenantAsync(ClaimsPrincipal user, int months, CancellationToken cancellationToken = default)
        {
            static ConsumptionLine Heat(decimal? current, decimal? lastYear, decimal adjusted) =>
                new(MeterType.Heat, "Heizung (Wärme)", MeasurementUnit.KilowattHours, current, null, lastYear, null, null, 0, "je m²", null, adjusted);

            IReadOnlyList<MonthlyConsumption> list =
            [
                new(new DateOnly(2026, 2, 1), [Heat(null, 800m, -10m)]),
                new(new DateOnly(2026, 1, 1), [Heat(500m, 1000m, 25m)]),
            ];
            return Task.FromResult<IReadOnlyList<TenantConsumption>>([new(Guid.NewGuid(), "Wohnung 1", list)]);
        }
    }
}
