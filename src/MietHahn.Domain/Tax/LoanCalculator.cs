namespace MietHahn.Domain.Tax;

/// <summary>Terms of an annuity loan for the repayment plan.</summary>
/// <param name="Principal">Amount paid out.</param>
/// <param name="DisbursementDate">Interest starts on this day.</param>
/// <param name="FirstPaymentDate">Due date of the first instalment; later instalments follow every <paramref name="MonthsPerPeriod"/> months.</param>
/// <param name="InterestRatePercent">Nominal interest rate per year.</param>
/// <param name="Instalment">Fixed instalment (interest + repayment) per period.</param>
/// <param name="MonthsPerPeriod">1 = monthly, 3 = quarterly.</param>
public sealed record LoanTerms(
    decimal Principal,
    DateOnly DisbursementDate,
    DateOnly FirstPaymentDate,
    decimal InterestRatePercent,
    decimal Instalment,
    int MonthsPerPeriod = 1);

/// <summary>Extra repayment, applied to the balance after the instalment of the period it falls into.</summary>
public sealed record ExtraRepayment(DateOnly Date, decimal Amount);

/// <summary>One instalment of the repayment plan.</summary>
public sealed record LoanPayment(DateOnly Date, decimal Interest, decimal Repayment, decimal ExtraRepayment, decimal Balance)
{
    public decimal Total => Interest + Repayment + ExtraRepayment;
}

/// <summary>Sums of one calendar year of the plan.</summary>
public sealed record LoanYear(int Year, decimal Interest, decimal Repayment, decimal ClosingBalance);

public sealed record LoanPlan(IReadOnlyList<LoanPayment> Payments, IReadOnlyList<LoanYear> Years, IReadOnlyList<string> Warnings);

/// <summary>
/// Repayment plan of an annuity loan with fixed instalment: interest per period on the outstanding balance
/// (German 30/360 day count, so a full month is 1/12 of the annual interest; a broken first period is pro rata),
/// the rest repays. Pure function without database access.
/// </summary>
public static class LoanCalculator
{
    private const int MaxPayments = 1200;

    /// <summary>Instalment for a given initial repayment rate (percent of the principal per year), as banks quote it.</summary>
    public static decimal InstalmentFor(decimal principal, decimal interestRatePercent, decimal initialRepaymentPercent, int monthsPerPeriod = 1) =>
        Math.Round(principal * (interestRatePercent + initialRepaymentPercent) / 100m * monthsPerPeriod / 12m, 2, MidpointRounding.AwayFromZero);

    /// <summary>German 30E/360 day count.</summary>
    public static int Days360(DateOnly from, DateOnly to)
    {
        var d1 = Math.Min(from.Day, 30);
        var d2 = Math.Min(to.Day, 30);
        return ((to.Year - from.Year) * 360) + ((to.Month - from.Month) * 30) + (d2 - d1);
    }

    public static LoanPlan Calculate(LoanTerms terms, IReadOnlyList<ExtraRepayment> extras)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(extras);

        var warnings = new List<string>();
        var payments = new List<LoanPayment>();
        var balance = terms.Principal;
        var from = terms.DisbursementDate;
        var due = terms.FirstPaymentDate;
        var pendingExtras = extras.OrderBy(e => e.Date).ToList();
        var checkedInstalment = false;

        while (balance > 0m && payments.Count < MaxPayments)
        {
            var interest = Math.Round(balance * terms.InterestRatePercent / 100m * Days360(from, due) / 360m, 2, MidpointRounding.AwayFromZero);
            if (!checkedInstalment)
            {
                checkedInstalment = true;
                if (terms.Instalment <= interest)
                {
                    warnings.Add("Die Rate deckt die Zinsen nicht: Das Darlehen wird so nie getilgt. Bitte die Rate prüfen.");
                    break;
                }
            }

            var repayment = Math.Min(terms.Instalment - interest, balance);
            balance -= repayment;
            var extra = 0m;
            while (pendingExtras.Count > 0 && pendingExtras[0].Date <= due)
            {
                extra += Math.Min(pendingExtras[0].Amount, balance - extra);
                pendingExtras.RemoveAt(0);
            }

            balance -= extra;
            payments.Add(new LoanPayment(due, interest, repayment, extra, balance));
            from = due;
            due = due.AddMonths(terms.MonthsPerPeriod);
        }

        if (payments.Count >= MaxPayments && balance > 0m)
        {
            warnings.Add("Der Tilgungsplan reicht über mehr als 100 Jahre; bitte die Rate prüfen.");
        }

        var years = payments
            .GroupBy(p => p.Date.Year)
            .Select(g => new LoanYear(g.Key, g.Sum(p => p.Interest), g.Sum(p => p.Repayment + p.ExtraRepayment), g.Last().Balance))
            .ToList();
        return new LoanPlan(payments, years, warnings);
    }
}
