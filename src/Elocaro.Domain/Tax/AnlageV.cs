using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Tax;

public enum AnlageVSection
{
    Income = 0,
    Expense = 1,
}

/// <summary>Category of income or income-related expenses in the Anlage V (Einkünfte aus Vermietung und Verpachtung).</summary>
public enum AnlageVCategory
{
    // Income
    BaseRent = 10,
    AllocatedCosts = 11,
    AdditionalPayments = 12,
    Refunds = 13,
    OtherIncome = 19,

    // Expenses
    Depreciation = 30,
    Interest = 31,
    FinancingCosts = 32,
    Maintenance = 33,
    PropertyTax = 34,
    AllocableOperatingCosts = 35,
    Insurance = 36,
    Administration = 37,
    Travel = 38,
    BankFees = 39,
    OtherExpenses = 49,
}

/// <param name="DefaultLine">Placeholder line number of the form; maintained per tax year because the lines change annually.</param>
public sealed record AnlageVCategoryInfo(AnlageVCategory Category, AnlageVSection Section, string Label, int DefaultLine);

public static class AnlageVCategories
{
    public static IReadOnlyList<AnlageVCategoryInfo> All { get; } =
    [
        new(AnlageVCategory.BaseRent, AnlageVSection.Income, "Mieteinnahmen (Kaltmiete)", 13),
        new(AnlageVCategory.AllocatedCosts, AnlageVSection.Income, "Vereinnahmte Umlagen und Vorauszahlungen", 14),
        new(AnlageVCategory.AdditionalPayments, AnlageVSection.Income, "Nachzahlungen aus der Nebenkostenabrechnung", 14),
        new(AnlageVCategory.Refunds, AnlageVSection.Income, "Erstattungen an Mieter (abzuziehen)", 14),
        new(AnlageVCategory.OtherIncome, AnlageVSection.Income, "Sonstige Einnahmen", 21),
        new(AnlageVCategory.Depreciation, AnlageVSection.Expense, "Absetzung für Abnutzung (AfA)", 33),
        new(AnlageVCategory.Interest, AnlageVSection.Expense, "Schuldzinsen", 35),
        new(AnlageVCategory.FinancingCosts, AnlageVSection.Expense, "Geldbeschaffungskosten (Finanzierungskosten)", 36),
        new(AnlageVCategory.Maintenance, AnlageVSection.Expense, "Erhaltungsaufwand (sofort und Verteilungsbeträge)", 37),
        new(AnlageVCategory.PropertyTax, AnlageVSection.Expense, "Grundsteuer", 40),
        new(AnlageVCategory.AllocableOperatingCosts, AnlageVSection.Expense, "Umlagefähige Betriebskosten (Wasser, Müll, Strom, Heizung …)", 41),
        new(AnlageVCategory.Insurance, AnlageVSection.Expense, "Versicherungen", 42),
        new(AnlageVCategory.Administration, AnlageVSection.Expense, "Verwaltungskosten", 43),
        new(AnlageVCategory.Travel, AnlageVSection.Expense, "Fahrtkosten", 44),
        new(AnlageVCategory.BankFees, AnlageVSection.Expense, "Kontoführungsgebühren", 45),
        new(AnlageVCategory.OtherExpenses, AnlageVSection.Expense, "Sonstige Werbungskosten", 46),
    ];

    public static AnlageVCategoryInfo Info(AnlageVCategory category) => All.Single(c => c.Category == category);

    /// <summary>Category of a cost type; <c>null</c> if the cost type does not count as an expense (capitalised or not a payment).</summary>
    public static AnlageVCategory? ForCostType(string costType) => costType switch
    {
        "betrkv-01" => AnlageVCategory.PropertyTax,
        "betrkv-13" => AnlageVCategory.Insurance,
        _ when costType.StartsWith("betrkv-", StringComparison.Ordinal) => AnlageVCategory.AllocableOperatingCosts,
        CostTypes.MaintenanceKey => null, // via maintenance service (immediate or distributed)
        "modernisierung" => null, // production costs: depreciation
        "leerstand" => null, // no payment, only allocation of vacancy
        "verwaltung" => AnlageVCategory.Administration,
        "fahrtkosten" => AnlageVCategory.Travel,
        "bankgebuehren" => AnlageVCategory.BankFees,
        "schuldzinsen" => AnlageVCategory.Interest,
        _ => AnlageVCategory.OtherExpenses,
    };

    /// <summary>Category of a payment on a receivable; <c>null</c> for deposits and balances brought forward (no income).</summary>
    public static AnlageVCategory? ForCharge(ChargeType kind, RentComponentType? component) => kind switch
    {
        ChargeType.Rent => component switch
        {
            RentComponentType.OperatingCostPrepayment or RentComponentType.HeatingCostPrepayment or RentComponentType.OperatingCostFlatRate => AnlageVCategory.AllocatedCosts,
            _ => AnlageVCategory.BaseRent,
        },
        ChargeType.AdditionalPayment => AnlageVCategory.AdditionalPayments,
        ChargeType.Deposit or ChargeType.BalanceCarriedForward => null,
        _ => AnlageVCategory.OtherIncome,
    };

    /// <summary>Interest, taxes and insurances recur regularly and fall under the 10-day rule.</summary>
    public static bool IsRecurring(AnlageVCategory category) =>
        category is AnlageVCategory.BaseRent or AnlageVCategory.AllocatedCosts or AnlageVCategory.PropertyTax or AnlageVCategory.Insurance
            or AnlageVCategory.Interest or AnlageVCategory.BankFees or AnlageVCategory.Administration;
}

/// <summary>Cash-basis principle (§ 11 EStG).</summary>
public static class CashBasisYear
{
    /// <summary>Days around the turn of the year within which recurring payments belong to the year of their due date (§ 11 Abs. 1 Satz 2 EStG).</summary>
    public const int WindowDays = 10;

    /// <summary>
    /// Year of inflow or outflow. Regularly recurring payments that are due and paid within ten days of the turn of the year belong
    /// to the year they are due in; everything else to the year of payment.
    /// </summary>
    public static int YearOf(DateOnly paidOn, DateOnly? dueOn, bool recurring) =>
        recurring && dueOn is { } due && TurnOf(due) is { } turn && TurnOf(paidOn) == turn ? due.Year : paidOn.Year;

    /// <summary>The year whose end the date is within <see cref="WindowDays"/> days of (22 December to 10 January), otherwise <c>null</c>.</summary>
    private static int? TurnOf(DateOnly date) => date switch
    {
        { Month: 12 } when date.Day > 31 - WindowDays => date.Year,
        { Month: 1 } when date.Day <= WindowDays => date.Year - 1,
        _ => null,
    };
}
