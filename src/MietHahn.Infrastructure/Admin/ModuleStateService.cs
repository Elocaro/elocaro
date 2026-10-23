using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

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
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var stored = await db.Set<ModuleSettings>().AsNoTracking().OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            usage = await CountUsageAsync(db, cancellationToken);
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
        usage = await CountUsageAsync(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>(), cancellationToken);
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
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var settings = await db.Set<ModuleSettings>().OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new ModuleSettings();
                db.Add(settings);
            }

            change(settings);
            await db.SaveChangesAsync(cancellationToken);
            usage = await CountUsageAsync(db, cancellationToken);
            current = ToSelection(settings);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<IReadOnlyDictionary<AppOption, int>> CountUsageAsync(MietHahnDbContext db, CancellationToken cancellationToken)
    {
        var models = await db.Set<Lease>().GroupBy(l => l.RentModel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var billing = await db.Set<Lease>().GroupBy(l => l.BillingType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var result = new Dictionary<AppOption, int>();
        foreach (var row in models.Where(m => m.Key != RentModel.Standard))
        {
            result[ModuleCatalog.Of(row.Key)] = row.Count;
        }

        foreach (var row in billing)
        {
            result[ModuleCatalog.Of(row.Key)] = row.Count;
        }

        var rooms = await db.Set<Room>().CountAsync(cancellationToken);
        if (rooms > 0)
        {
            result[AppOption.RoomRental] = rooms;
        }

        foreach (var row in await db.Set<Unit>().GroupBy(u => u.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
        {
            if (row.Key == UnitType.ParkingSpace)
            {
                result[AppOption.UnitParking] = row.Count;
            }
            else if (row.Key == UnitType.Commercial)
            {
                result[AppOption.UnitCommercial] = row.Count;
            }
        }

        var deposits = await db.Set<Lease>().CountAsync(l => l.Deposit != null && l.Deposit > 0, cancellationToken);
        if (deposits > 0)
        {
            result[AppOption.LeaseDeposit] = deposits;
        }

        var guarantors = await db.Set<LeaseParty>().CountAsync(p => p.Role == PartyRole.Guarantor, cancellationToken);
        if (guarantors > 0)
        {
            result[AppOption.LeaseGuarantor] = guarantors;
        }

        if (await db.Set<FinanceSettings>().OrderBy(f => f.CreatedAt).FirstOrDefaultAsync(cancellationToken) is { } finance)
        {
            if (finance.ReminderFee > 0 || finance.FirstNoticeFee > 0 || finance.SecondNoticeFee > 0)
            {
                result[AppOption.DunningFees] = 1;
            }

            if (finance.ChargeDefaultInterest || finance.BaseRate is not null)
            {
                result[AppOption.DunningInterest] = 1;
            }
        }

        var loans = await db.Set<Loan>().CountAsync(cancellationToken);
        if (loans > 0)
        {
            result[AppOption.TaxLoans] = loans;
        }

        var depreciation = await db.Set<PropertyAcquisition>().CountAsync(cancellationToken) + await db.Set<MaintenanceDistribution>().CountAsync(cancellationToken);
        if (depreciation > 0)
        {
            result[AppOption.TaxDepreciation] = depreciation;
        }

        var owners = await db.Set<IdentityUserRole<Guid>>()
            .Join(db.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .CountAsync(name => name == Roles.Owner, cancellationToken);
        if (owners > 0)
        {
            result[AppOption.TaxOwnerPortal] = owners;
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
