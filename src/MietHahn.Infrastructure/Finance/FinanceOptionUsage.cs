using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Admin;
using MietHahn.Domain.Finance;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Finance;

/// <summary>Whether dunning fees and default interest are set up.</summary>
internal sealed class FinanceOptionUsage(MietHahnDbContext db) : IOptionUsageSource
{
    public async Task<IReadOnlyDictionary<AppOption, int>> CountAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<AppOption, int>();
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

        return result;
    }
}
