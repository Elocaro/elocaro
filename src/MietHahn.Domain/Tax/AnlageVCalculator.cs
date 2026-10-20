namespace MietHahn.Domain.Tax;

public enum AnlageVSource
{
    Receipt = 0,
    Payment = 1,
    Depreciation = 2,
    Loan = 3,
    Maintenance = 4,
}

/// <summary>A single amount of the evaluation with its origin (drilldown).</summary>
/// <param name="Date">Date of inflow or outflow (payment date).</param>
/// <param name="SourceId">Receipt, payment, loan or property, depending on <paramref name="Source"/>.</param>
/// <param name="LeaseId">Only for payments: the lease (link to the tenant account).</param>
public sealed record AnlageVPosting(AnlageVCategory Category, DateOnly Date, string Text, decimal Amount, AnlageVSource Source, Guid? SourceId = null, Guid? LeaseId = null);

public sealed record AnlageVLineResult(AnlageVCategoryInfo Info, int LineNumber, decimal Amount, IReadOnlyList<AnlageVPosting> Postings);

/// <param name="DeductibleShare">Share of the expenses that is deductible (cheap letting), <c>null</c> if all of them are.</param>
public sealed record AnlageVResult(IReadOnlyList<AnlageVLineResult> Income, IReadOnlyList<AnlageVLineResult> Expenses, decimal? DeductibleShare = null)
{
    public decimal IncomeTotal => Income.Sum(l => l.Amount);

    public decimal ExpenseTotal => Expenses.Sum(l => l.Amount);

    /// <summary>Part of the expenses that is not deductible because of a cheap letting (§ 21 Abs. 2 EStG).</summary>
    public decimal Reduction => DeductibleShare is { } share ? ExpenseTotal - Math.Round(ExpenseTotal * share, 2, MidpointRounding.AwayFromZero) : 0m;

    public decimal DeductibleExpenses => ExpenseTotal - Reduction;

    public decimal Surplus => IncomeTotal - DeductibleExpenses;
}

/// <summary>Anlage V evaluation: sums the postings per category and maps them to the form lines of the tax year.</summary>
public static class AnlageVCalculator
{
    /// <summary>Rent below this share of the local comparative rent: the income-related expenses are only deductible proportionally (§ 21 Abs. 2 EStG).</summary>
    public const decimal ReducedRentPercent = 66m;

    public const decimal ProportionalRentPercent = 50m;

    public static AnlageVResult Calculate(IEnumerable<AnlageVPosting> postings, IReadOnlyDictionary<AnlageVCategory, int> lineNumbers, decimal? deductibleShare = null)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(lineNumbers);
        var byCategory = postings.ToLookup(p => p.Category);
        AnlageVLineResult Line(AnlageVCategoryInfo info)
        {
            var items = byCategory[info.Category].OrderBy(p => p.Date).ThenBy(p => p.Text, StringComparer.Ordinal).ToList();
            return new AnlageVLineResult(info, lineNumbers.GetValueOrDefault(info.Category, info.DefaultLine), items.Sum(p => p.Amount), items);
        }

        return new AnlageVResult(
            AnlageVCategories.All.Where(c => c.Section == AnlageVSection.Income).Select(Line).ToList(),
            AnlageVCategories.All.Where(c => c.Section == AnlageVSection.Expense).Select(Line).ToList(),
            deductibleShare);
    }

    /// <summary>
    /// Share of the expenses that is deductible for a cheap letting; <c>null</c> if all of them are. Below 50 % of the local rent the
    /// letting is split into a paid and an unpaid part. From 50 % to below 66 % the full deduction needs a positive total surplus
    /// forecast; without it the expenses are reduced proportionally as well.
    /// </summary>
    public static decimal? DeductibleShare(decimal? rentPercentOfLocal, bool surplusForecastPositive) => rentPercentOfLocal switch
    {
        null => null,
        < ProportionalRentPercent => rentPercentOfLocal / 100m,
        < ReducedRentPercent when !surplusForecastPositive => rentPercentOfLocal / 100m,
        _ => null,
    };

    /// <summary>Hint for cheap lettings; <c>null</c> if the rent is at least 66 % of the local comparative rent.</summary>
    public static string? ReducedRentHint(decimal? rentPercentOfLocal, bool surplusForecastPositive = false) => rentPercentOfLocal switch
    {
        null => null,
        < ProportionalRentPercent =>
            $"Verbilligte Vermietung ({rentPercentOfLocal:0.0} % der ortsüblichen Miete, unter 50 %): Die Vermietung wird in einen entgeltlichen und einen unentgeltlichen Teil aufgeteilt. " +
            "Die Werbungskosten sind deshalb nur anteilig abziehbar und wurden entsprechend gekürzt (§ 21 Abs. 2 EStG).",
        < ReducedRentPercent when surplusForecastPositive =>
            $"Verbilligte Vermietung ({rentPercentOfLocal:0.0} % der ortsüblichen Miete, 50 % bis unter 66 %): Bei positiver Totalüberschussprognose bleiben die Werbungskosten voll abziehbar (§ 21 Abs. 2 EStG).",
        < ReducedRentPercent =>
            $"Verbilligte Vermietung ({rentPercentOfLocal:0.0} % der ortsüblichen Miete, 50 % bis unter 66 %): Der volle Abzug setzt eine positive Totalüberschussprognose voraus. " +
            "Ohne sie sind die Werbungskosten anteilig gekürzt; bei positiver Prognose „Totalüberschussprognose positiv“ anhaken (§ 21 Abs. 2 EStG).",
        _ => null,
    };
}
