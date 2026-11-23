using System.Security.Claims;
using Bunit;
using Elocaro.Application.Portfolio;
using Elocaro.Domain.Portfolio;
using Elocaro.Web.Components.Pages.Admin.Properties;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Elocaro.Web.Tests.Components;

public class KeyFiguresSectionTests : BunitContext
{
    [Fact]
    public async Task The_history_shows_all_figures_per_year_and_the_chart_follows_the_chosen_one()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IPropertyKeyFigures>(new Figures());
        Services.AddSingleton<IRentPotential>(new NoPotential());
        Services.AddSingleton<TimeProvider>(new FixedTime());
        var user = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity("test"))));
        var cut = Render<KeyFiguresSection>(p => p.Add(c => c.PropertyId, Guid.NewGuid()).AddCascadingValue(user));
        cut.WaitForAssertion(() => cut.Find("#key-figures"));
        Assert.Empty(cut.FindAll("#history-table"));

        await cut.Find("button.button").ClickAsync();

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("#history-table tbody tr").Count));
        Assert.Contains("Cashflow je Jahr", cut.Find("#history-chart").InnerHtml, StringComparison.Ordinal);
        Assert.Contains("–", cut.Find("#history-table tbody tr").InnerHtml, StringComparison.Ordinal); // no investment: no yield
    }

    private sealed class Figures : IPropertyKeyFigures
    {
        public Task<PropertyKeyFigures?> GetAsync(ClaimsPrincipal user, Guid propertyId, int year, CancellationToken cancellationToken = default) =>
            Task.FromResult<PropertyKeyFigures?>(PropertyKeyFigureCalculator.Calculate(new PropertyFigureInput(year, 12000m, 14000m, 1000m, 2000m, 500m, 0m, 800m, null, 365, 73, 14000m, 0m)));
    }

    private sealed class NoPotential : IRentPotential
    {
        public Task<RentPotential?> GetAsync(ClaimsPrincipal user, Guid propertyId, CancellationToken cancellationToken = default) => Task.FromResult<RentPotential?>(null);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    }
}
