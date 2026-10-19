namespace MietHahn.Domain.Finance;

/// <summary>Eine Forderung (Soll) oder ein Guthaben (Haben) für die Verrechnung, Betrag stets positiv.</summary>
/// <param name="IsRent">Monatsmiete (für <see cref="AllocationOrder.RentFirst"/>).</param>
/// <param name="Designation">Nur bei Guthaben: Forderung, die zuerst getilgt werden soll.</param>
public sealed record AllocationItem(Guid Id, DateOnly Date, decimal Amount, AllocationRank Rank = AllocationRank.PrincipalClaim, bool IsRent = false, Guid? Designation = null);

/// <summary>Teil eines Guthabens, der eine Forderung tilgt; <see cref="Date"/> ist der Tag des Guthabens (Zufluss).</summary>
public sealed record AllocationShare(Guid CreditId, Guid ClaimId, decimal Amount, DateOnly Date);

public sealed record AllocationResult(IReadOnlyList<AllocationShare> Shares, IReadOnlyDictionary<Guid, decimal> Open, decimal Overpayment)
{
    public decimal Settled(Guid claimId) => Shares.Where(a => a.ClaimId == claimId).Sum(a => a.Amount);
}

/// <summary>
/// Verteilt Zahlungen und Gutschriften auf Forderungen. Ein Guthaben tilgt zuerst die bestimmte Forderung, dann die
/// zu seinem Datum fälligen Forderungen in der gewählten Reihenfolge. Was übrig bleibt (Vorauszahlung, Überzahlung),
/// tilgt spätere Forderungen, sobald sie fällig werden. Forderungen gleichen Rangs und gleicher Fälligkeit (z. B. die
/// Bestandteile einer Monatsmiete) werden anteilig getilgt.
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

        // Zeitlich: am selben Tag werden Forderungen vor den Zahlungen fällig.
        var events = target.Select(f => (f.Date, Kind: 0, Items: f))
            .Concat(creditItems.Select(g => (g.Date, Kind: 1, Items: g)))
            .OrderBy(e => e.Date).ThenBy(e => e.Kind).ThenBy(e => e.Items.Id);

        // Alle Ereignisse eines Tages und einer Art gemeinsam, damit z. B. alle Bestandteile einer Monatsmiete offen sind.
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

        // Anteilig nach offenen Beträgen, Rundungsreste in Cent an die größten Nachkommareste.
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
