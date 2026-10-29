using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Documents;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Documents;

public class DocumentReadinessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task A_fresh_installation_lacks_the_landlord_profile_and_the_template()
    {
        var issues = await WithAsync(r => r.CheckAsync(TemplateKind.Contract, null, Ct));

        Assert.Equal([ReadinessIssueKind.LandlordProfile, ReadinessIssueKind.Template], issues.Select(i => i.Kind));
        Assert.All(issues, i => Assert.True(i.Blocking));
        Assert.Equal("/verwaltung/einstellungen/vermieter", issues[0].Link);
    }

    [Fact]
    public async Task Only_what_every_document_needs_is_checked_without_a_kind()
    {
        var issues = await WithAsync(r => r.CheckAsync(null, null, Ct));

        Assert.Equal([ReadinessIssueKind.LandlordProfile], issues.Select(i => i.Kind));
    }

    [Fact]
    public async Task Profile_and_default_templates_resolve_the_blocking_issues()
    {
        await SaveProfileAsync();
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDocumentTemplates>().InstallDefaultsAsync(Ct);
        }

        Assert.Empty(await WithAsync(r => r.CheckAsync(TemplateKind.Contract, null, Ct)));
        Assert.Empty(await WithAsync(r => r.CheckAsync(TemplateKind.RentIncrease, null, Ct)));
        Assert.Empty(await WithAsync(r => r.CheckAsync(TemplateKind.Letter, null, Ct)));
    }

    [Fact]
    public async Task A_missing_living_area_is_a_warning_for_contracts_and_gone_once_entered()
    {
        await SaveProfileAsync();
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDocumentTemplates>().InstallDefaultsAsync(Ct);
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var person = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { LastName = "Schmidt" }, null, Ct));
        var lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 1, 1), TenantIds = [person], BaseRent = 650m }, Ct));

        var warning = Assert.Single(await WithAsync(r => r.CheckAsync(TemplateKind.Contract, lease, Ct)));
        Assert.Equal(ReadinessIssueKind.LivingArea, warning.Kind);
        Assert.False(warning.Blocking);
        Assert.Equal($"/verwaltung/einheiten/{unit}", warning.Link);
        Assert.Contains("Wohnung 1", warning.Message, StringComparison.Ordinal);
        // Other document kinds do not need the area.
        Assert.Empty(await WithAsync(r => r.CheckAsync(TemplateKind.RentIncrease, lease, Ct)));

        Validate(await properties.SetUnitAreaAsync(unit, new AreaInput { From = new DateOnly(2020, 1, 1), Area = 55m }, Ct));
        Assert.Empty(await WithAsync(r => r.CheckAsync(TemplateKind.Contract, lease, Ct)));
    }

    private async Task SaveProfileAsync()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILandlordProfileService>().SaveAsync(
            new LandlordProfileInput { Name = "Hausverwaltung Beispiel", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<T>(Func<IDocumentReadiness, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDocumentReadiness>());
    }
}
