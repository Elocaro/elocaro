namespace Elocaro.Domain.Finance;

/// <summary>A claim (debit) or credit for allocation; amount is always positive.</summary>
/// <param name="IsRent">Monthly rent (for <see cref="AllocationOrder.RentFirst"/>).</param>
/// <param name="Designation">Credits only: claim that should be settled first.</param>
public sealed record AllocationItem(Guid Id, DateOnly Date, decimal Amount, AllocationRank Rank = AllocationRank.PrincipalClaim, bool IsRent = false, Guid? Designation = null);

/// <summary>Part of a credit that settles a claim; <see cref="Date"/> is the day of the credit (inflow).</summary>
public sealed record AllocationShare(Guid CreditId, Guid ClaimId, decimal Amount, DateOnly Date);

public sealed record AllocationResult(IReadOnlyList<AllocationShare> Shares, IReadOnlyDictionary<Guid, decimal> Open, decimal Overpayment)
{
    public decimal Settled(Guid claimId) => Shares.Where(a => a.ClaimId == claimId).Sum(a => a.Amount);
}

/// <summary>
/// Distributes payments and credit notes across claims. A credit first settles the designated claim, then the
/// claims due on its date in the chosen order. Whatever is left over (prepayment, overpayment)
/// settles later claims as they fall due. Claims of equal rank and equal due date (e.g. the
/// components of a monthly rent) are settled proportionally.
/// </summary>
public static class Allocation
{
    public static AllocationResult Distribute(IEnumerable<AllocationItem> claims, IEnumerable<AllocationItem> credit, AllocationOrder order)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(credit);
        var target = claims.ToList();
        var creditItems = credit.ToList();
        if (target.Concat(creditItems).Any(p => p.Amount <= 0))
        {
            throw new ArgumentException("Beträge der Verrechnungsposten müssen positiv sein.");
        }

        var open = target.ToDictionary(f => f.Id, f => f.Amount);
        var rest = creditItems.ToDictionary(g => g.Id, g => g.Amount);
        var shares = new List<AllocationShare>();
        var due = new List<AllocationItem>();
        var pool = new List<AllocationItem>();

        // Chronologically: on the same day, claims fall due before payments.
        var events = target.Select(f => (f.Date, Kind: 0, Items: f))
            .Concat(creditItems.Select(g => (g.Date, Kind: 1, Items: g)))
            .OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Items.Id);

        // All events of one day and kind together, so that e.g. all components of a monthly rent are open.
        foreach (var group in events.GroupBy(e => (e.Date, e.Kind)))
        {
            foreach (var (_, kind, items) in group)
            {
                if (kind == 0)
                {
                    due.Add(items);
                }
                else
                {
                    pool.Add(items);
                    if (items.Designation is { } specific && open.TryGetValue(specific, out var openDesignated) && openDesignated > 0)
                    {
                        Post(items, specific, Math.Min(rest[items.Id], openDesignated));
                    }
                }
            }

            Settle();
        }

        return new AllocationResult(shares, open, rest.Values.Sum());

        void Settle()
        {
            while (true)
            {
                var credit = pool.FirstOrDefault(g => rest[g.Id] > 0);
                var openItems = due.Where(f => open[f.Id] > 0).ToList();
                if (credit is null || openItems.Count == 0)
                {
                    return;
                }

                var next = openItems.GroupBy(f => Key(f, order)).OrderBy(g => g.Key).First().ToList();
                var amount = Math.Min(rest[credit.Id], next.Sum(f => open[f.Id]));
                foreach (var (claim, part) in ProRata(next, amount))
                {
                    Post(credit, claim, part);
                }
            }
        }

        void Post(AllocationItem credit, Guid claim, decimal amount)
        {
            if (amount <= 0)
            {
                return;
            }

            rest[credit.Id] -= amount;
            open[claim] -= amount;
            shares.Add(new AllocationShare(credit.Id, claim, amount, credit.Date));
        }

        // Proportional to open amounts; rounding remainders in cents go to the largest fractional remainders.
        IEnumerable<(Guid, decimal)> ProRata(List<AllocationItem> group, decimal amount)
        {
            var total = group.Sum(f => open[f.Id]);
            if (amount == total)
            {
                return group.Select(f => (f.Id, open[f.Id])).ToList();
            }

            var raw = group.Select(f => (f.Id, Value: amount * open[f.Id] / total)).ToList();
            var roundedDown = raw.Select(r => (r.Id, r.Value, Cent: decimal.Floor(r.Value * 100) / 100)).ToList();
            var remaining = (int)Math.Round((amount - roundedDown.Sum(a => a.Cent)) * 100);
            var surcharge = roundedDown.OrderByDescending(a => a.Value - a.Cent).ThenBy(a => a.Id).Take(remaining).Select(a => a.Id).ToHashSet();
            return roundedDown.Select(a => (a.Id, a.Cent + (surcharge.Contains(a.Id) ? 0.01m : 0m))).ToList();
        }
    }

    private static (int, DateOnly) Key(AllocationItem f, AllocationOrder order) => order switch
    {
        AllocationOrder.Statutory => ((int)f.Rank, f.Date),
        AllocationOrder.RentFirst => (f.IsRent ? 0 : 1, f.Date),
        _ => (0, f.Date),
    };
}
