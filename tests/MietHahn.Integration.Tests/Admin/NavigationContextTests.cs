using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.People;
using MietHahn.Application.Portfolio;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Admin;

public class NavigationContextTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid unitId;
    private Guid roomId;
    private Guid personId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Id(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        unitId = Id(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 4" }, Ct));
        roomId = Id(await properties.CreateRoomAsync(unitId, new RoomInput { Name = "Zimmer 1" }, Ct));
        personId = Id(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task A_room_leads_back_through_unit_and_property()
    {
        var context = await ResolveAsync($"/verwaltung/zimmer/{roomId}");

        Assert.Equal(propertyId, context!.PropertyId);
        Assert.Equal(["Objekte", "Lindenweg 3", "Wohnung 4", "Zimmer 1"], context.Trail.Select(s => s.Label));
        Assert.Equal($"/verwaltung/einheiten/{unitId}", context.Trail[2].Href);
    }

    [Fact]
    public async Task A_page_of_a_property_carries_its_summary()
    {
        var context = await ResolveAsync($"/verwaltung/zimmer/{roomId}");

        Assert.Equal("Lindenweg 3", context!.Property!.Name);
        Assert.Equal("Lindenweg 3, 50667 Köln", context.Property.Address);
        Assert.Equal(1, context.Property.UnitCount);
    }

    [Fact]
    public async Task Subpages_of_a_property_resolve_to_the_property()
    {
        var context = await ResolveAsync($"/verwaltung/objekte/{propertyId}/heizung");

        Assert.Equal(propertyId, context!.PropertyId);
        Assert.Equal(["Objekte", "Lindenweg 3"], context.Trail.Select(s => s.Label));
    }

    [Fact]
    public async Task A_loan_names_its_property_but_keeps_the_tabs_of_Geld()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var loanId = Id(await scope.ServiceProvider.GetRequiredService<ILoanService>().CreateAsync(new LoanInput
        {
            PropertyId = propertyId,
            Bank = "Sparkasse",
            Principal = 200000m,
            DisbursementDate = new DateOnly(2020, 1, 1),
            FirstPaymentDate = new DateOnly(2020, 2, 1),
            InterestRatePercent = 1.5m,
            Instalment = 900m,
        }, Ct));

        var context = await ResolveAsync($"/verwaltung/finanzen/darlehen/{loanId}");

        Assert.Null(context!.PropertyId);
        Assert.Equal(["Objekte", "Lindenweg 3", "Darlehen Sparkasse"], context.Trail.Select(s => s.Label));
    }

    [Fact]
    public async Task A_person_has_no_property()
    {
        var context = await ResolveAsync($"/verwaltung/personen/{personId}");

        Assert.Null(context!.PropertyId);
        Assert.Equal(["Personen", "Anna Schmidt"], context.Trail.Select(s => s.Label));
    }

    [Theory]
    [InlineData("/verwaltung/objekte")]
    [InlineData("/verwaltung/personen/aenderungen")]
    [InlineData("/verwaltung/zimmer/00000000-0000-0000-0000-000000000001")]
    public async Task Pages_without_an_entity_have_no_context(string path) => Assert.Null(await ResolveAsync(path));

    private static Guid Id(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<PageContext?> ResolveAsync(string path)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INavigationContext>().ResolveAsync(path, Ct);
    }
}
