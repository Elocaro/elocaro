using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;

namespace Elocaro.Infrastructure.Finance;

/// <summary>
/// The part of the rent a tenant withholds under a reduction the landlord disputes. It stays visible on the reduction but is not
/// dunned and carries no default interest: the open items of the month are lowered by it.
/// </summary>
internal static class DisputedAmounts
{
    /// <summary>Disputed amount per service month up to the current month, calculated like the credit of an acknowledged reduction.</summary>
    public static Dictionary<DateOnly, decimal> ByMonth(IEnumerable<Charge> rents, IEnumerable<RentReduction> reductions, Period leaseTerm, DateOnly today)
    {
        var rentByMonth = rents.Where(s => s is { Kind: ChargeType.Rent, Amount: > 0, ServiceMonth: not null })
            .GroupBy(s => s.ServiceMonth!.Value).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var result = new Dictionary<DateOnly, decimal>();
        foreach (var reduction in reductions.Where(r => r.IsDisputed))
        {
            var last = reduction.To is { } to && to < today ? to : today;
            for (var month = new DateOnly(reduction.From.Year, reduction.From.Month, 1); month <= last; month = month.AddMonths(1))
            {
                if (!rentByMonth.TryGetValue(month, out var rent) || Period.Month(month.Year, month.Month).Intersect(leaseTerm) is not { } active)
                {
                    continue;
                }

                var amount = reduction.AmountFor(month, rent, active);
                if (amount > 0)
                {
                    result[month] = Math.Min(rent, result.GetValueOrDefault(month) + amount);
                }
            }
        }

        return result;
    }

    /// <summary>Lowers the open amounts of the rents of each month by the disputed amount (components in the order of the account).</summary>
    public static List<(Charge Charge, decimal Open, decimal Disputed)> Apply(IEnumerable<(Charge Charge, decimal Open)> items, IReadOnlyDictionary<DateOnly, decimal> disputed)
    {
        var remaining = new Dictionary<DateOnly, decimal>(disputed);
        var result = new List<(Charge, decimal, decimal)>();
        foreach (var (charge, open) in items.OrderBy(i => i.Charge.Component is { } c ? (int)c : 100))
        {
            var taken = 0m;
            if (charge is { Kind: ChargeType.Rent, ServiceMonth: { } month } && remaining.TryGetValue(month, out var left) && left > 0)
            {
                taken = Math.Min(open, left);
                remaining[month] = left - taken;
            }

            result.Add((charge, open - taken, taken));
        }

        return result;
    }
}
