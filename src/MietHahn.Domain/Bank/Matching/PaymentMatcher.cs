using System.Globalization;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.Bank.Matching;

/// <param name="DefaultPropertyId">Expenses: the only property of the bank account, if there is exactly one.</param>
public sealed record MatchInput(
    DateOnly BookingDate,
    decimal Amount,
    string? CounterpartyName,
    string? CounterpartyIban,
    string Purpose,
    Guid? DefaultPropertyId = null);

/// <param name="Ibans">Bank details stored for the tenant.</param>
public sealed record TenantInfo(string? FirstName, string LastName, string? Company, IReadOnlyList<string> Ibans);

/// <summary>Due, not yet fully paid charge of a lease; <see cref="ServiceMonth"/> only for monthly rents.</summary>
public sealed record OpenCharge(Guid ChargeId, ChargeType Kind, DateOnly Due, DateOnly? ServiceMonth, decimal Open, string Name);

/// <param name="RentAt">Total monthly rent (all components) valid on a day.</param>
public sealed record LeaseCandidate(
    Guid LeaseId,
    string Description,
    string UnitName,
    DateOnly Start,
    DateOnly? End,
    IReadOnlyList<TenantInfo> Tenants,
    Func<DateOnly, decimal> RentAt,
    IReadOnlyList<OpenCharge> OpenCharges);

/// <summary>Earlier expense assignment, used to recognize recurring payees.</summary>
public sealed record ExpenseHistory(string? CounterpartyIban, string? CounterpartyName, string CostType, Guid PropertyId, Guid? UnitId, bool Allocable);

public sealed record MatchContext(IReadOnlyList<MatchRule> Rules, IReadOnlyList<LeaseCandidate> Leases, IReadOnlyList<ExpenseHistory> History);

public enum SuggestionKind
{
    Lease = 0,
    Expense = 1,
    Ignore = 2,
}

/// <summary>Part of an incoming payment; with <see cref="ChargeId"/> the part settles exactly this charge, otherwise the allocation order decides.</summary>
public sealed record SuggestedPart(Guid? ChargeId, decimal Amount, string Text);

public sealed record MatchSuggestion(SuggestionKind Kind, int Score, IReadOnlyList<string> Reasons)
{
    public Guid? RuleId { get; init; }

    public string? RuleName { get; init; }

    public Guid? LeaseId { get; init; }

    public IReadOnlyList<SuggestedPart> Parts { get; init; } = [];

    public string? CostType { get; init; }

    public Guid? PropertyId { get; init; }

    public Guid? UnitId { get; init; }

    public bool? Allocable { get; init; }

    public string Confidence => Score >= 70 ? "hoch" : Score >= 50 ? "mittel" : "niedrig";
}

/// <summary>
/// Matching engine: ranks assignment suggestions for a bank transaction. User rules apply first (score 100), then for
/// income the leases are scored by counter IBAN, name, amount and purpose; for expenses earlier assignments of the
/// same payee are used. Pure logic without any dependency on persistence.
/// </summary>
public static class PaymentMatcher
{
    /// <summary>Candidates below this score are not suggested.</summary>
    public const int MinimumScore = 35;

    private const int MaxSuggestions = 5;

    public static IReadOnlyList<MatchSuggestion> Suggest(MatchInput transaction, MatchContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(context);
        var result = new List<MatchSuggestion>();
        var months = TextMatching.Months(transaction.Purpose, transaction.BookingDate);

        var rule = context.Rules.OrderBy(r => r.Priority).FirstOrDefault(r => r.Matches(transaction));
        if (rule is not null)
        {
            var fromRule = FromRule(rule, transaction, context, months);
            if (fromRule is not null)
            {
                result.Add(fromRule);
            }
        }

        if (transaction.Amount > 0)
        {
            result.AddRange(context.Leases
                .Select(lease => ScoreLease(lease, transaction, months))
                .Where(s => s.Score >= MinimumScore && !result.Any(r => r.LeaseId == s.LeaseId))
                .OrderByDescending(s => s.Score).ThenBy(s => s.LeaseId)
                .Take(MaxSuggestions));
        }
        else if (transaction.Amount < 0 && rule is null && FromHistory(transaction, context.History) is { } history)
        {
            result.Add(history);
        }

        return result.Take(MaxSuggestions).ToList();
    }

    private static MatchSuggestion? FromRule(MatchRule rule, MatchInput transaction, MatchContext context, IReadOnlyList<DateOnly> months)
    {
        var reasons = new List<string> { $"Regel „{rule.Name}“" };
        switch (rule.Action)
        {
            case RuleAction.Ignore:
                return new MatchSuggestion(SuggestionKind.Ignore, 100, reasons) { RuleId = rule.Id, RuleName = rule.Name };
            case RuleAction.Expense when transaction.Amount < 0:
                return new MatchSuggestion(SuggestionKind.Expense, 100, reasons)
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    CostType = rule.CostType,
                    PropertyId = rule.PropertyId ?? transaction.DefaultPropertyId,
                    UnitId = rule.UnitId,
                    Allocable = rule.Allocable,
                };
            case RuleAction.Lease when rule.LeaseId is { } leaseId:
                {
                    var lease = context.Leases.FirstOrDefault(l => l.LeaseId == leaseId);
                    if (lease is null)
                    {
                        return null;
                    }

                    return new MatchSuggestion(SuggestionKind.Lease, 100, reasons)
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        LeaseId = leaseId,
                        Parts = Parts(lease, transaction, months),
                    };
                }

            default:
                return null;
        }
    }

    private static MatchSuggestion? FromHistory(MatchInput transaction, IReadOnlyList<ExpenseHistory> history)
    {
        if (transaction.CounterpartyIban is { Length: > 0 } iban)
        {
            var compact = Common.Iban.Compact(iban);
            var byIban = history.FirstOrDefault(h => h.CounterpartyIban is not null && Common.Iban.Compact(h.CounterpartyIban) == compact);
            if (byIban is not null)
            {
                return FromHistory(byIban, 75, "Früher Zahlung an dieselbe Gegen-IBAN");
            }
        }

        var name = TextMatching.Normalize(transaction.CounterpartyName);
        if (name.Length > 0)
        {
            var byName = history.FirstOrDefault(h => TextMatching.Normalize(h.CounterpartyName) == name);
            if (byName is not null)
            {
                return FromHistory(byName, 60, "Früher Zahlung an dieselbe Gegenseite");
            }
        }

        return null;
    }

    private static MatchSuggestion FromHistory(ExpenseHistory entry, int score, string reason) =>
        new(SuggestionKind.Expense, score, [$"{reason}: {CostTypes.Find(entry.CostType)?.Name ?? entry.CostType}"])
        {
            CostType = entry.CostType,
            PropertyId = entry.PropertyId,
            UnitId = entry.UnitId,
            Allocable = entry.Allocable,
        };

    private static MatchSuggestion ScoreLease(LeaseCandidate lease, MatchInput transaction, IReadOnlyList<DateOnly> months)
    {
        var score = 0;
        var reasons = new List<string>();
        var counterTokens = TextMatching.Tokens(transaction.CounterpartyName);
        var purposeTokens = TextMatching.Tokens(transaction.Purpose);

        // Counter IBAN of the tenant's stored bank details
        if (transaction.CounterpartyIban is { Length: > 0 } iban
            && lease.Tenants.Any(t => t.Ibans.Any(i => Common.Iban.Compact(i) == Common.Iban.Compact(iban))))
        {
            score += 45;
            reasons.Add("Gegen-IBAN gehört zum Mieter");
        }

        // Name of the counterparty (or, weaker, in the purpose)
        var (namePoints, nameReason) = lease.Tenants.Count == 0 ? (0, null) : lease.Tenants.Select(t => NameScore(t, counterTokens)).MaxBy(n => n.Points);
        if (namePoints > 0)
        {
            score += namePoints;
            reasons.Add(nameReason!);
        }
        else if (lease.Tenants.Any(t => NameScore(t, purposeTokens).Points > 0))
        {
            score += 8;
            reasons.Add("Mieter im Verwendungszweck genannt");
        }

        // Amount
        var groups = Groups(lease.OpenCharges, transaction.BookingDate);
        var (amountPoints, amountReason) = AmountScore(lease, groups, transaction);
        if (amountPoints > 0)
        {
            score += amountPoints;
            reasons.Add(amountReason!);
        }

        // Purpose: unit, month, "Miete"
        if (TextMatching.ContainsUnit(transaction.Purpose, lease.UnitName))
        {
            score += 15;
            reasons.Add($"„{lease.UnitName}“ im Verwendungszweck");
        }

        var leaseMonths = months.Where(m => m >= new DateOnly(lease.Start.Year, lease.Start.Month, 1) && (lease.End is not { } end || m <= end)).ToList();
        if (leaseMonths.Count > 0)
        {
            score += 10;
            reasons.Add("Zeitraum " + string.Join(", ", leaseMonths.Select(m => m.ToString("MM/yyyy", CultureInfo.InvariantCulture))) + " im Verwendungszweck");
        }

        if (purposeTokens.Any(t => t is "miete" or "kaltmiete" or "warmmiete" or "nebenkosten" or "vorauszahlung" or "mietzahlung" or "nk" or "bk"))
        {
            score += 5;
            reasons.Add("Miete im Verwendungszweck");
        }

        // Booking far outside the lease term
        if (transaction.BookingDate < lease.Start.AddDays(-31))
        {
            score -= 30;
            reasons.Add("Vor Mietbeginn");
        }
        else if (lease.End is { } end2 && transaction.BookingDate > end2.AddDays(120))
        {
            score -= 10;
            reasons.Add("Lange nach Mietende");
        }

        return new MatchSuggestion(SuggestionKind.Lease, Math.Clamp(score, 0, 100), reasons)
        {
            LeaseId = lease.LeaseId,
            Parts = Parts(lease, transaction, months),
        };
    }

    private static (int Points, string? Reason) NameScore(TenantInfo tenant, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return (0, null);
        }

        var best = (Points: 0, Reason: (string?)null);
        if (!string.IsNullOrWhiteSpace(tenant.Company))
        {
            var company = TextMatching.Tokens(tenant.Company);
            if (company.Count > 0 && company.All(c => tokens.Any(t => TextMatching.TokenMatches(t, c))))
            {
                best = (30, $"Name passt ({tenant.Company})");
            }
        }

        var last = TextMatching.Tokens(tenant.LastName);
        if (last.Count > 0 && last.All(l => tokens.Any(t => TextMatching.TokenMatches(t, l))))
        {
            var firstTokens = TextMatching.Tokens(tenant.FirstName);
            var first = firstTokens.Count > 0 ? firstTokens[0] : null;
            var firstMatches = first is not null && tokens.Any(t => TextMatching.TokenMatches(t, first) || (t.Length == 1 && t[0] == first[0]));
            var points = firstMatches ? 32 : 24;
            if (points > best.Points)
            {
                best = (points, firstMatches ? $"Name passt ({tenant.FirstName} {tenant.LastName})" : $"Nachname passt ({tenant.LastName})");
            }
        }

        return best;
    }

    /// <summary>Open charges per month (monthly rents of one month together) in due order; charges long in the future are not yet relevant.</summary>
    private static List<ChargeGroup> Groups(IReadOnlyList<OpenCharge> charges, DateOnly booking) =>
        charges.Where(c => c.Open > 0 && c.Due <= booking.AddDays(45))
            .GroupBy(c => c.ServiceMonth is { } month ? (object)month : c.ChargeId)
            .Select(g => new ChargeGroup(g.ToList()))
            .OrderBy(g => g.Due).ThenBy(g => g.Charges[0].ChargeId)
            .ToList();

    private static (int Points, string? Reason) AmountScore(LeaseCandidate lease, List<ChargeGroup> groups, MatchInput transaction)
    {
        var amount = transaction.Amount;
        if (groups.Count > 0)
        {
            if (groups[0].Open == amount)
            {
                return (25, $"Betrag entspricht der offenen Forderung ({groups[0].Text})");
            }

            if (groups.FirstOrDefault(g => g.Open == amount) is { } single)
            {
                return (22, $"Betrag entspricht der offenen Forderung ({single.Text})");
            }

            var running = 0m;
            for (var i = 0; i < groups.Count; i++)
            {
                running += groups[i].Open;
                if (running == amount)
                {
                    return (22, $"Betrag entspricht den ältesten {i + 1} offenen Forderungen (Sammelzahlung)");
                }
            }
        }

        return lease.RentAt(transaction.BookingDate) == amount ? (18, "Betrag entspricht der Monatsmiete") : (0, null);
    }

    /// <summary>Proposed split of an incoming payment: explicit charges for collective payments or named months, otherwise one free part.</summary>
    private static List<SuggestedPart> Parts(LeaseCandidate lease, MatchInput transaction, IReadOnlyList<DateOnly> months)
    {
        var amount = transaction.Amount;
        var groups = Groups(lease.OpenCharges, transaction.BookingDate);

        var named = groups.Where(g => g.ServiceMonth is { } m && months.Contains(m)).ToList();
        if (named.Count > 0 && named.Sum(g => g.Open) == amount && (named.Count > 1 || named[0] != groups[0]))
        {
            return Explicit(named);
        }

        var running = 0m;
        for (var i = 0; i < groups.Count; i++)
        {
            running += groups[i].Open;
            if (running == amount)
            {
                return i == 0 ? [new SuggestedPart(null, amount, "Zahlung auf das Mieterkonto")] : Explicit(groups.Take(i + 1));
            }
        }

        return [new SuggestedPart(null, amount, "Zahlung auf das Mieterkonto")];
    }

    private static List<SuggestedPart> Explicit(IEnumerable<ChargeGroup> groups) =>
        groups.SelectMany(g => g.Charges).Select(c => new SuggestedPart(c.ChargeId, c.Open, c.Name)).ToList();

    private sealed class ChargeGroup(List<OpenCharge> charges)
    {
        public List<OpenCharge> Charges { get; } = charges;

        public DateOnly Due => Charges.Min(c => c.Due);

        public DateOnly? ServiceMonth => Charges[0].ServiceMonth;

        public decimal Open => Charges.Sum(c => c.Open);

        public string Text => ServiceMonth is { } month ? $"Miete {month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}" : Charges[0].Name;
    }
}
