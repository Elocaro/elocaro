using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tax;

/// <summary>Whether loans and depreciation (acquisitions, distributed maintenance) are in use.</summary>
internal sealed class TaxOptionUsage(MietHahnDbContext db) : IOptionUsageSource
{
    public async Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AppOption, int>();
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

        return result;
    }
}
