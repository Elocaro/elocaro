using Elocaro.Domain.Tax;

namespace Elocaro.Domain.Tests.Tax;

public class LoanCalculatorTests
{
    // 200,000 € at 3.60 %, instalment 1,000 €/month, paid out and first due 1 Feb 2024 -> hand-calculated (30/360):
    // month 1: interest 200,000 * 3.6 % / 12 = 600.00, repayment 400.00, balance 199,600.00
    // month 2: interest 199,600 * 0.003 = 598.80, repayment 401.20, balance 199,198.80
    private static readonly LoanTerms Terms = new(200_000m, new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 1), 3.6m, 1_000m);

    [Fact]
    public void First_instalments_match_hand_calculation()
    {
        var plan = LoanCalculator.Calculate(Terms, []);

        Assert.Equal(new LoanPayment(new DateOnly(2024, 2, 1), 600m, 400m, 0m, 199_600m), plan.Payments[0]);
        Assert.Equal(new LoanPayment(new DateOnly(2024, 3, 1), 598.80m, 401.20m, 0m, 199_198.80m), plan.Payments[1]);
    }

    [Fact]
    public void Plan_ends_with_a_zero_balance_and_interest_plus_repayment_equals_payments()
    {
        var plan = LoanCalculator.Calculate(Terms, []);

        Assert.Equal(0m, plan.Payments[^1].Balance);
        Assert.Equal(Terms.Principal, plan.Payments.Sum(p => p.Repayment));
        Assert.All(plan.Payments.Take(plan.Payments.Count - 1), p => Assert.Equal(1_000m, p.Total));
        Assert.True(plan.Payments[^1].Total <= 1_000m);
    }

    [Fact]
    public void Year_interest_is_the_sum_of_the_instalments_of_the_year()
    {
        var plan = LoanCalculator.Calculate(Terms, []);
        var year = plan.Years.Single(j => j.Year == 2024);

        Assert.Equal(plan.Payments.Where(p => p.Date.Year == 2024).Sum(p => p.Interest), year.Interest);
        Assert.Equal(11, plan.Payments.Count(p => p.Date.Year == 2024));
        Assert.Equal(plan.Payments.Last(p => p.Date.Year == 2024).Balance, year.ClosingBalance);
    }

    [Fact]
    public void Broken_first_period_is_charged_pro_rata_30_360()
    {
        // 16 Jan -> 1 Feb: 15 days (30E/360) -> 100,000 * 6 % * 15/360 = 250.00
        var terms = new LoanTerms(100_000m, new DateOnly(2024, 1, 16), new DateOnly(2024, 2, 1), 6m, 700m);

        Assert.Equal(15, LoanCalculator.Days360(terms.DisbursementDate, terms.FirstPaymentDate));
        Assert.Equal(250m, LoanCalculator.Calculate(terms, []).Payments[0].Interest);
    }

    [Fact]
    public void Quarterly_instalments_charge_a_quarter_of_the_annual_interest()
    {
        var terms = Terms with { Instalment = 3_000m, MonthsPerPeriod = 3, FirstPaymentDate = new DateOnly(2024, 4, 1) };
        var plan = LoanCalculator.Calculate(terms, []);

        Assert.Equal(1_800m, plan.Payments[0].Interest);
        Assert.Equal(new DateOnly(2024, 7, 1), plan.Payments[1].Date);
    }

    [Fact]
    public void Extra_repayment_reduces_the_balance_and_shortens_the_plan()
    {
        var without = LoanCalculator.Calculate(Terms, []);
        var with = LoanCalculator.Calculate(Terms, [new ExtraRepayment(new DateOnly(2024, 6, 15), 20_000m)]);

        var july = with.Payments.Single(p => p.Date == new DateOnly(2024, 7, 1));
        Assert.Equal(20_000m, july.ExtraRepayment);
        Assert.True(with.Payments.Count < without.Payments.Count);
        Assert.True(with.Payments.Sum(p => p.Interest) < without.Payments.Sum(p => p.Interest));
    }

    [Fact]
    public void Instalment_below_interest_warns_and_yields_no_plan()
    {
        var plan = LoanCalculator.Calculate(Terms with { Instalment = 500m }, []);

        Assert.Empty(plan.Payments);
        Assert.Single(plan.Warnings);
    }

    [Fact]
    public void Instalment_from_initial_repayment_rate()
    {
        // (3.6 % + 2 %) of 200,000 = 11,200 a year = 933.33 a month
        Assert.Equal(933.33m, LoanCalculator.InstalmentFor(200_000m, 3.6m, 2m));
        Assert.Equal(2_800m, LoanCalculator.InstalmentFor(200_000m, 3.6m, 2m, 3));
    }
}
