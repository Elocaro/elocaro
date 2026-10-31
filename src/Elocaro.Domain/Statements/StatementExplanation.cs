using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;

namespace Elocaro.Domain.Statements;

/// <summary>
/// Plain-language derivation of a cost line for the tenant. Uses only the values frozen in the statement snapshot
/// (nothing is recalculated from current master data); a statement that lacks a value simply omits the sentence.
/// </summary>
public static class StatementExplainer
{
    /// <summary>E.g. "1.240,00 € nach Wohnfläche: Ihr Anteil 248,00 €".</summary>
    public static string Summary(TenantCostLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var key = string.IsNullOrWhiteSpace(line.Key) ? "" : $" nach {line.Key}";
        return $"{GermanText.Money(line.CostTotal)}{key}: Ihr Anteil {GermanText.Money(line.Share)}";
    }

    /// <summary>Subject of a question about a statement or one of its positions, e.g. "Frage zur Abrechnung 2026: Grundsteuer".</summary>
    public static string QuestionSubject(Period billingPeriod, string? position = null)
    {
        var label = billingPeriod.From is { Month: 1, Day: 1 } && billingPeriod.To is { Month: 12, Day: 31 } to && to.Year == billingPeriod.From.Year
            ? billingPeriod.From.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{billingPeriod.From:dd.MM.yyyy}–{billingPeriod.To:dd.MM.yyyy}";
        var subject = string.IsNullOrWhiteSpace(position) ? $"Frage zur Abrechnung {label}" : $"Frage zur Abrechnung {label}: {position}";
        return subject.Length > 200 ? subject[..200] : subject;
    }

    /// <summary>Further sentences of the derivation: the tenant's share of the key and costs that are not charged.</summary>
    public static IReadOnlyList<string> Details(TenantCostLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(line.Basis))
        {
            details.Add($"Ihr Anteil am Schlüssel: {line.Basis}");
        }

        if (line.NotAgreed > 0)
        {
            details.Add($"Die Kostenart ist im Mietvertrag nicht vereinbart; {GermanText.Money(line.NotAgreed)} werden Ihnen nicht berechnet.");
        }

        return details;
    }
}

/// <summary>One cost type compared with the previous year. <c>null</c> = the cost type did not exist in that year.</summary>
public sealed record LineComparison(string CostType, string Name, decimal? Current, decimal? Previous)
{
    /// <summary>Current minus previous share (a missing side counts as 0).</summary>
    public decimal Difference => (Current ?? 0m) - (Previous ?? 0m);
}

/// <param name="PreviousDays">Days of the tenancy in the previous year's billing period.</param>
/// <param name="Days">Days of the tenancy in this year's billing period.</param>
public sealed record YearComparison(Period PreviousPeriod, int PreviousDays, int Days, IReadOnlyList<LineComparison> Lines, decimal PreviousShare, decimal Share)
{
    /// <summary>The tenancy covers a different number of days than in the previous year: the figures are not directly comparable.</summary>
    public bool TermDiffers => PreviousDays != Days;

    public decimal Difference => Share - PreviousShare;
}

/// <summary>Comparison of a tenant's cost lines with the statement of the previous year for the same lease. Amounts only, no causes.</summary>
public static class StatementComparer
{
    public static YearComparison Compare(Period previousPeriod, TenantStatement current, TenantStatement previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        var now = Totals(current);
        var before = Totals(previous);
        var rows = new List<LineComparison>();
        foreach (var (key, line) in now)
        {
            rows.Add(new LineComparison(key, line.Name, line.Share, before.TryGetValue(key, out var old) ? old.Share : null));
        }

        foreach (var (key, old) in before.Where(p => !now.ContainsKey(p.Key)))
        {
            rows.Add(new LineComparison(key, old.Name, null, old.Share));
        }

        return new YearComparison(previousPeriod, previous.Days, current.Days, rows, previous.Lines.Sum(l => l.Share), current.Lines.Sum(l => l.Share));
    }

    private static Dictionary<string, (string Name, decimal Share)> Totals(TenantStatement statement) =>
        statement.Lines.GroupBy(l => l.CostType).ToDictionary(g => g.Key, g => (g.First().Name, Share: g.Sum(l => l.Share)));
}
