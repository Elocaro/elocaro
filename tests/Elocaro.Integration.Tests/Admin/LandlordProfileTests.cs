using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

public class LandlordProfileTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static LandlordProfileInput Input(string name) => new() { Name = name, Street = "Musterweg 1", PostalCode = "12345", City = "Musterstadt" };

    [Fact]
    public async Task A_further_profile_needs_the_standard_one_first_and_is_listed_after_it()
    {
        Assert.False((await WithAsync<ILandlordProfileService, Result>(s => s.SaveAsync(null, Input("Zweiter"), Ct))).Succeeded);

        await WithAsync<ILandlordProfileService, bool>(async s => { await s.SaveAsync(Input("Andy Hahn"), Ct); return true; });
        var id = Validate(await WithAsync<ILandlordProfileService, Result>(s => s.SaveAsync(null, Input("Eigentümergemeinschaft"), Ct)));

        var rows = await WithAsync<ILandlordProfileService, IReadOnlyList<LandlordProfileRow>>(s => s.ListAsync(Ct));
        Assert.Equal([("Andy Hahn", true), ("Eigentümergemeinschaft", false)], rows.Select(r => (r.Name, r.IsDefault)));
        Assert.Equal("Eigentümergemeinschaft", (await WithAsync<ILandlordProfileService, LandlordProfileInput?>(s => s.GetAsync(id, Ct)))!.Name);
        Assert.Equal("Andy Hahn", (await WithAsync<ILandlordProfileService, LandlordProfileInput?>(s => s.GetAsync(Ct)))!.Name);

        Validate(await WithAsync<ILandlordProfileService, Result>(s => s.SaveAsync(id, Input("Eigentümergemeinschaft Lindenweg"), Ct)));
        Assert.Equal("Eigentümergemeinschaft Lindenweg", (await WithAsync<ILandlordProfileService, LandlordProfileInput?>(s => s.GetAsync(id, Ct)))!.Name);
    }

    [Fact]
    public async Task The_standard_profile_and_profiles_in_use_cannot_be_deleted()
    {
        await WithAsync<ILandlordProfileService, bool>(async s => { await s.SaveAsync(Input("Andy Hahn"), Ct); return true; });
        var other = Validate(await WithAsync<ILandlordProfileService, Result>(s => s.SaveAsync(null, Input("Gemeinschaft"), Ct)));
        var standard = (await WithAsync<ILandlordProfileService, IReadOnlyList<LandlordProfileRow>>(s => s.ListAsync(Ct))).Single(r => r.IsDefault).Id;
        var house = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln", LandlordProfileId = other }, Ct)));

        Assert.Contains("Standardprofil", (await WithAsync<ILandlordProfileService, Result>(s => s.DeleteAsync(standard, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Contains("Objekt", (await WithAsync<ILandlordProfileService, Result>(s => s.DeleteAsync(other, Ct))).Errors.Single(), StringComparison.Ordinal);
        Assert.Equal(1, (await WithAsync<ILandlordProfileService, IReadOnlyList<LandlordProfileRow>>(s => s.ListAsync(Ct))).Single(r => r.Id == other).Properties);

        var property = (await WithAsync<IPropertyService, PropertyDetails?>(s => s.GetAsync(house, Ct)))!.Data;
        property.LandlordProfileId = null;
        Validate(await WithAsync<IPropertyService, Result>(s => s.UpdateAsync(house, property, Ct)));
        Validate(await WithAsync<ILandlordProfileService, Result>(s => s.DeleteAsync(other, Ct)));
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Id!.Value;
    }
}
