using Elocaro.Application.Admin;
using Elocaro.Domain.Admin;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

public class ModuleStateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private IModuleState State => environment.Services.GetRequiredService<IModuleState>();

    [Fact]
    public async Task Without_a_stored_selection_everything_is_on_and_the_wizard_is_open()
    {
        var selection = await State.GetAsync(Ct);

        Assert.Equal(Enum.GetValues<AppModule>().Length, selection.Enabled.Count);
        Assert.Null(selection.SetupCompletedAt);
        Assert.True(State.IsEnabled(AppModule.Bank));
    }

    [Fact]
    public async Task A_saved_selection_is_applied_and_switches_on_required_modules()
    {
        await State.SaveAsync([AppModule.OperatingCosts], completeSetup: true, cancellationToken: Ct);

        Assert.True(State.IsEnabled(AppModule.OperatingCosts));
        Assert.True(State.IsEnabled(AppModule.Receipts));
        Assert.False(State.IsEnabled(AppModule.Bank));
        Assert.True(State.SetupCompleted);
    }

    [Fact]
    public async Task The_selection_is_stored_and_changing_it_does_not_reopen_the_wizard()
    {
        await State.SaveAsync([AppModule.Bank], completeSetup: true, cancellationToken: Ct);
        await State.SaveAsync([AppModule.Bank, AppModule.Tickets], completeSetup: false, cancellationToken: Ct);

        await using var scope = environment.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<ModuleSettings>().SingleAsync(Ct);

        Assert.DoesNotContain("Bank", stored.DisabledModules);
        Assert.DoesNotContain("Tickets", stored.DisabledModules);
        Assert.Contains("Dunning", stored.DisabledModules);
        Assert.NotNull(stored.SetupCompletedAt);
    }
}
