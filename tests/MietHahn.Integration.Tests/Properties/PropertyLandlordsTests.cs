using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Properties;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Properties;

public class PropertyLandlordsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;
    private Guid house;
    private Guid first;
    private Guid second;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        house = Validate(await WithAsync<IPropertyService, Result>(s => s.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)));
        await WithAsync<ILandlordProfileService, bool>(async s =>
        {
            await s.SaveAsync(Profile("Erbengemeinschaft Muster", "Notdienst Alt"), Ct);
            return true;
        });
        second = Validate(await WithAsync<ILandlordProfileService, Result>(s => s.SaveAsync(null, Profile("Hans Neu", "Notdienst Neu"), Ct)));
        first = (await WithAsync<ILandlordProfileService, IReadOnlyList<LandlordProfileRow>>(s => s.ListAsync(Ct))).Single(p => p.IsDefault).Id;
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static LandlordProfileInput Profile(string name, string emergency) =>
        new() { Name = name, Street = "Musterweg 1", PostalCode = "12345", City = "Musterstadt", EmergencyInfo = emergency };

    [Fact]
    public async Task A_dated_entry_replaces_the_profile_from_its_day_on_and_can_be_corrected()
    {
        Assert.Equal("Notdienst Alt", await EmergencyAsync());
        var history = await WithAsync<IPropertyLandlords, LandlordHistory>(l => l.GetAsync(house, Ct));
        Assert.Equal(("Erbengemeinschaft Muster", 0), (history.BaseProfileName, history.Entries.Count));

        // Future change: nothing happens yet.
        var future = Validate(await WithAsync<IPropertyLandlords, Result>(l => l.AddAsync(house, new LandlordChangeInput { ProfileId = second, From = new DateOnly(2099, 3, 1) }, Ct)));
        Assert.Equal("Notdienst Alt", await EmergencyAsync());

        // Corrected to a day in the past: the new landlord applies.
        Validate(await WithAsync<IPropertyLandlords, Result>(l => l.UpdateAsync(house, future, new LandlordChangeInput { ProfileId = second, From = new DateOnly(2020, 3, 1) }, Ct)));
        Assert.Equal("Notdienst Neu", await EmergencyAsync());
        Assert.True((await WithAsync<IPropertyLandlords, LandlordHistory>(l => l.GetAsync(house, Ct))).Entries.Single().Current);

        // One entry per day; a profile in use cannot be deleted; removing the entry restores the old landlord.
        Assert.False((await WithAsync<IPropertyLandlords, Result>(l => l.AddAsync(house, new LandlordChangeInput { ProfileId = first, From = new DateOnly(2020, 3, 1) }, Ct))).Succeeded);
        Assert.False((await WithAsync<ILandlordProfileService, Result>(s => s.DeleteAsync(second, Ct))).Succeeded);
        Validate(await WithAsync<IPropertyLandlords, Result>(l => l.RemoveAsync(house, future, Ct)));
        Assert.Equal("Notdienst Alt", await EmergencyAsync());
    }

    private Task<string?> EmergencyAsync() => WithAsync<ILandlordProfileService, string?>(s => s.EmergencyInfoAsync(house, Ct));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
