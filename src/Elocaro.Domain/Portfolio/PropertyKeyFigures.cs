using Elocaro.Domain.Common;

namespace Elocaro.Domain.Portfolio;

/// <summary>The numbers one property and year are measured with; all of them come from the books, nothing is estimated.</summary>
/// <param name="BaseRent">Net rent received (Anlage V, cash basis).</param>
/// <param name="TotalIncome">All income received: net rent, allocated costs, additional payments, refunds, other.</param>
/// <param name="NonAllocableCosts">Running costs the landlord bears himself: maintenance, administration, travel, bank fees, other.</param>
/// <param name="AllocableCosts">Costs that are passed on to tenants: operating costs, property tax, insurance.</param>
/// <param name="Interest">Interest on the loans of the property (share of the property).</param>
/// <param name="OneOffFinancingCosts">One-off financing costs of the year.</param>
/// <param name="Repayment">Repayment of the loans of the property (share of the property).</param>
/// <param name="Investment">Purchase price plus ancillary costs; <c>null</c> if the acquisition is not entered.</param>
/// <param name="RentableUnitDays">Days of the year summed over all rentable units.</param>
/// <param name="VacantUnitDays">Of them not let.</param>
/// <param name="DueInYear">Claims due in the year (debit side of the tenant accounts).</param>
/// <param name="ArrearsOfYear">Of them still unpaid today.</param>
public sealed record PropertyFigureInput(
    int Year,
    decimal BaseRent,
    decimal TotalIncome,
    decimal NonAllocableCosts,
    decimal AllocableCosts,
    decimal Interest,
    decimal OneOffFinancingCosts,
    decimal Repayment,
    decimal? Investment,
    int RentableUnitDays,
    int VacantUnitDays,
    decimal DueInYear,
    decimal ArrearsOfYear);

/// <summary>A line of the calculation: the amount and how it came about, so the figure can be followed.</summary>
public sealed record FigureStep(string Label, decimal Amount, string? Formula = null);

/// <param name="NetYield">Net rental yield in percent; <c>null</c> without investment.</param>
/// <param name="CashflowBeforeRepayment">Income minus expenses without depreciation (interest included).</param>
/// <param name="Cashflow">After repayment.</param>
/// <param name="VacancyPercent">Share of the unit days without a lease; <c>null</c> without rentable units.</param>
/// <param name="ArrearsPercent">Share of the claims due in the year that is unpaid; <c>null</c> if nothing was due.</param>
public sealed record PropertyKeyFigures(
    int Year,
    decimal? NetYield,
    decimal CashflowBeforeRepayment,
    decimal Cashflow,
    decimal? VacancyPercent,
    decimal? ArrearsPercent,
    IReadOnlyList<FigureStep> Steps);

public static class PropertyKeyFigureCalculator
{
    public static PropertyKeyFigures Calculate(PropertyFigureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var steps = new List<FigureStep>();

        decimal? netYield = null;
        var netRent = input.BaseRent - input.NonAllocableCosts;
        steps.Add(new FigureStep("Nettomiete", netRent, "Kaltmiete − nicht umlagefähige Kosten"));
        if (input.Investment is { } investment and > 0)
        {
            netYield = Math.Round(netRent / investment * 100m, 2, MidpointRounding.AwayFromZero);
            steps.Add(new FigureStep("Nettomietrendite (%)", netYield.Value, "Nettomiete ÷ (Kaufpreis + Kaufnebenkosten) × 100"));
        }

        var expenses = input.NonAllocableCosts + input.AllocableCosts + input.Interest + input.OneOffFinancingCosts;
        var beforeRepayment = input.TotalIncome - expenses;
        steps.Add(new FigureStep("Einnahmen", input.TotalIncome, "Zahlungseingänge des Jahres (Anlage V)"));
        steps.Add(new FigureStep("Ausgaben ohne AfA", expenses, "nicht umlagefähige und umlagefähige Kosten, Zinsen, Finanzierungskosten"));
        steps.Add(new FigureStep("Cashflow vor Tilgung", beforeRepayment, "Einnahmen − Ausgaben"));
        steps.Add(new FigureStep("Tilgung", input.Repayment));
        var cashflow = beforeRepayment - input.Repayment;
        steps.Add(new FigureStep("Cashflow", cashflow, "Cashflow vor Tilgung − Tilgung"));

        decimal? vacancy = null;
        if (input.RentableUnitDays > 0)
        {
            vacancy = Math.Round((decimal)input.VacantUnitDays / input.RentableUnitDays * 100m, 1, MidpointRounding.AwayFromZero);
            steps.Add(new FigureStep("Leerstandsquote (%)", vacancy.Value, $"{input.VacantUnitDays} unvermietete Tage ÷ {input.RentableUnitDays} Einheitentage × 100"));
        }

        decimal? arrears = null;
        if (input.DueInYear > 0)
        {
            arrears = Math.Round(input.ArrearsOfYear / input.DueInYear * 100m, 1, MidpointRounding.AwayFromZero);
            steps.Add(new FigureStep("Rückstandsquote (%)", arrears.Value, $"{input.ArrearsOfYear:0.00} € offen ÷ {input.DueInYear:0.00} € fällig × 100"));
        }

        return new PropertyKeyFigures(input.Year, netYield, beforeRepayment, cashflow, vacancy, arrears, steps);
    }

    /// <summary>Days of a year on which a unit has no lease: the year minus the union of the lease periods.</summary>
    public static int VacantDays(int year, IEnumerable<Period> leasePeriods)
    {
        ArgumentNullException.ThrowIfNull(leasePeriods);
        var yearPeriod = Period.CalendarYear(year);
        var covered = leasePeriods.Select(p => p.Intersect(yearPeriod)).OfType<Period>().OrderBy(p => p.From).ToList();
        var occupied = 0;
        DateOnly? end = null;
        foreach (var period in covered)
        {
            var from = end is { } e && period.From <= e ? e.AddDays(1) : period.From;
            var to = period.To ?? yearPeriod.To!.Value;
            if (to >= from)
            {
                occupied += to.DayNumber - from.DayNumber + 1;
                end = to;
            }
        }

        return yearPeriod.Days - occupied;
    }
}
