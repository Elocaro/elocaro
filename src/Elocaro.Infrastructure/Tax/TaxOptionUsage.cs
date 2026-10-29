using Elocaro.Application.Admin;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Tax;

/// <summary>Whether loans and depreciation (acquisitions, distributed maintenance) are in use.</summary>
internal sealed class TaxOptionUsage(ElocaroDbContext db) : IOptionUsageSource
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
