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

public sealed record AnlageVResult(IReadOnlyList<AnlageVLineResult> Income, IReadOnlyList<AnlageVLineResult> Expenses)
{
    public decimal IncomeTotal => Income.Sum(l => l.Amount);

    public decimal ExpenseTotal => Expenses.Sum(l => l.Amount);

    public decimal Surplus => IncomeTotal - ExpenseTotal;
}

/// <summary>Anlage V evaluation: sums the postings per category and maps them to the form lines of the tax year.</summary>
public static class AnlageVCalculator
{
    /// <summary>Rent below this share of the local comparative rent: the income-related expenses are only deductible proportionally (§ 21 Abs. 2 EStG).</summary>
    public const decimal ReducedRentPercent = 66m;

    public const decimal ProportionalRentPercent = 50m;

    public static AnlageVResult Calculate(IEnumerable<AnlageVPosting> postings, IReadOnlyDictionary<AnlageVCategory, int> lineNumbers)
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
            AnlageVCategories.All.Where(c => c.Section == AnlageVSection.Expense).Select(Line).ToList());
    }

    /// <summary>Hint for cheap lettings; <c>null</c> if the rent is at least 66 % of the local comparative rent.</summary>
    public static string? ReducedRentHint(decimal? rentPercentOfLocal) => rentPercentOfLocal switch
    {
        null => null,
        < ProportionalRentPercent =>
            "Verbilligte Vermietung unter 50 % der ortsüblichen Miete: Die Vermietung gilt in einen entgeltlichen und einen unentgeltlichen Teil aufgeteilt; " +
            "die Werbungskosten sind nur anteilig abziehbar (§ 21 Abs. 2 EStG). Bitte das Verhältnis vor dem Eintragen kürzen.",
        < ReducedRentPercent =>
            "Miete zwischen 50 % und 66 % der ortsüblichen Miete: Die Totalüberschussprognose ist zu prüfen; bei negativer Prognose sind die Werbungskosten anteilig zu kürzen (§ 21 Abs. 2 EStG).",
        _ => null,
    };
}
