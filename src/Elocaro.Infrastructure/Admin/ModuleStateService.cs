using Elocaro.Application.Admin;
using Elocaro.Domain.Admin;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Infrastructure.Admin;

internal sealed class ModuleStateService(IServiceScopeFactory scopes, TimeProvider clock) : IModuleState, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile ModuleSelection? current;
    private volatile IReadOnlyDictionary<AppOption, int> usage = new Dictionary<AppOption, int>();

    public bool SetupCompleted => current is null || current.SetupCompletedAt is not null;

    public bool IsEnabled(AppModule module) => current?.Enabled.Contains(module) ?? true;

    public bool IsOptionEnabled(AppOption option) => current?.EnabledOptions.Contains(option) ?? true;

    public bool IsVisible(AppOption option) =>
        IsOptionEnabled(option) && (ModuleCatalog.Info(option).Module is not { } module || IsEnabled(module))
        || UsageCount(option) > 0;

    public int UsageCount(AppOption option) => usage.GetValueOrDefault(option);

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (current is not null)
        {
            return;
        }

        await GetAsync(cancellationToken);
    }

    public async Task<ModuleSelection> GetAsync(CancellationToken cancellationToken = default)
    {
        if (current is { } cached)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            var stored = await db.Set<ModuleSettings>().AsNoTracking().OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            usage = await CountUsageAsync(scope.ServiceProvider, cancellationToken);
            return current = ToSelection(stored);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RefreshUsageAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        usage = await CountUsageAsync(scope.ServiceProvider, cancellationToken);
    }

    public async Task SaveAsync(IEnumerable<AppModule> enabled, bool completeSetup, IEnumerable<AppOption>? options = null, CancellationToken cancellationToken = default)
    {
        var selection = ModuleCatalog.WithRequirements(enabled);
        var optionList = options?.ToHashSet();
        await Update(settings =>
        {
            settings.DisabledModules = Enum.GetValues<AppModule>().Where(m => !selection.Contains(m)).Select(m => m.ToString()).ToList();
            if (optionList is not null)
            {
                settings.DisabledOptions = Enum.GetValues<AppOption>().Where(o => !optionList.Contains(o)).Select(o => o.ToString()).ToList();
            }

            if (completeSetup)
            {
                settings.SetupCompletedAt = clock.GetUtcNow();
            }
        }, cancellationToken);
    }

    public Task SkipSetupAsync(CancellationToken cancellationToken = default) =>
        Update(settings => settings.SetupCompletedAt ??= clock.GetUtcNow(), cancellationToken);

    private async Task Update(Action<ModuleSettings> change, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
            var settings = await db.Set<ModuleSettings>().OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new ModuleSettings();
                db.Add(settings);
            }

            change(settings);
            await db.SaveChangesAsync(cancellationToken);
            usage = await CountUsageAsync(scope.ServiceProvider, cancellationToken);
            current = ToSelection(settings);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Asks every module for the options its data uses; this service itself knows none of them.</summary>
    private static async Task<IReadOnlyDictionary<AppOption, int>> CountUsageAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = new Dictionary<AppOption, int>();
        foreach (var source in services.GetServices<IOptionUsageSource>())
        {
            foreach (var (option, count) in await source.CountAsync(cancellationToken))
            {
                result[option] = result.GetValueOrDefault(option) + count;
            }
        }

        return result;
    }

    public void Dispose() => gate.Dispose();

    private static ModuleSelection ToSelection(ModuleSettings? stored)
    {
        var disabled = stored?.DisabledModules ?? [];
        var disabledOptions = stored?.DisabledOptions ?? [];
        var enabled = Enum.GetValues<AppModule>().Where(m => !disabled.Contains(m.ToString())).ToHashSet();
        var options = Enum.GetValues<AppOption>().Where(o => !disabledOptions.Contains(o.ToString())).ToHashSet();
        return new ModuleSelection(enabled, options, stored?.SetupCompletedAt);
    }
}
