using Elocaro.Application.Admin;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Leases;

/// <summary>Which rent models, kinds of billing, deposits and guarantors the leases use.</summary>
internal sealed class LeaseOptionUsage(ElocaroDbContext db) : IOptionUsageSource
{
    public async Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AppOption, int>();
        var models = await db.Set<Lease>().GroupBy(l => l.RentModel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        foreach (var row in models.Where(m => m.Key != RentModel.Standard))
        {
            result[ModuleCatalog.Of(row.Key)] = row.Count;
        }

        var billing = await db.Set<Lease>().GroupBy(l => l.BillingType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        foreach (var row in billing)
        {
            result[ModuleCatalog.Of(row.Key)] = row.Count;
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

        return result;
    }
}
