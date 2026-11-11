using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Tax;

namespace Elocaro.Domain.Tests.Tax;

public class AnlageVTests
{
    [Fact]
    public void Recurring_rent_for_january_paid_on_28_december_belongs_to_the_following_year() =>
        Assert.Equal(2027, CashBasisYear.YearOf(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 6), recurring: true));

    [Fact]
    public void Recurring_payment_due_in_december_but_paid_in_january_belongs_to_the_due_year() =>
        Assert.Equal(2026, CashBasisYear.YearOf(new DateOnly(2027, 1, 5), new DateOnly(2026, 12, 28), recurring: true));

    [Theory]
    [InlineData("2026-12-20", "2027-01-05")] // paid before the window opens
    [InlineData("2026-12-28", "2027-01-15")] // due after the window closes
    [InlineData("2026-06-01", "2026-06-03")]
    public void Outside_the_ten_day_window_the_payment_year_counts(string paid, string due) =>
        Assert.Equal(DateOnly.Parse(paid, System.Globalization.CultureInfo.InvariantCulture).Year,
            CashBasisYear.YearOf(DateOnly.Parse(paid, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), recurring: true));

    [Fact]
    public void Non_recurring_payment_always_counts_in_the_payment_year() =>
        Assert.Equal(2026, CashBasisYear.YearOf(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 6), recurring: false));

    [Fact]
    public void Payment_without_due_date_counts_in_the_payment_year() =>
        Assert.Equal(2026, CashBasisYear.YearOf(new DateOnly(2026, 12, 28), null, recurring: true));

    [Fact]
    public void Charges_and_cost_types_are_classified()
    {
        Assert.Equal(AnlageVCategory.BaseRent, AnlageVCategories.ForCharge(ChargeType.Rent, RentComponentType.BaseRent));
        Assert.Equal(AnlageVCategory.AllocatedCosts, AnlageVCategories.ForCharge(ChargeType.Rent, RentComponentType.HeatingCostPrepayment));
        Assert.Equal(AnlageVCategory.AdditionalPayments, AnlageVCategories.ForCharge(ChargeType.AdditionalPayment, null));
        Assert.Null(AnlageVCategories.ForCharge(ChargeType.Deposit, null));
        Assert.Equal(AnlageVCategory.PropertyTax, AnlageVCategories.ForCostType("betrkv-01"));
        Assert.Equal(AnlageVCategory.Insurance, AnlageVCategories.ForCostType("betrkv-13"));
        Assert.Equal(AnlageVCategory.AllocableOperatingCosts, AnlageVCategories.ForCostType("betrkv-02"));
        Assert.Null(AnlageVCategories.ForCostType("modernisierung"));
    }

    [Fact]
    public void Calculator_sums_per_line_and_uses_the_configured_line_numbers()
    {
        var postings = new[]
        {
            new AnlageVPosting(AnlageVCategory.BaseRent, new DateOnly(2026, 2, 1), "Kaltmiete 02/2026", 620m, AnlageVSource.Payment),
            new AnlageVPosting(AnlageVCategory.BaseRent, new DateOnly(2026, 1, 1), "Kaltmiete 01/2026", 620m, AnlageVSource.Payment),
            new AnlageVPosting(AnlageVCategory.PropertyTax, new DateOnly(2026, 3, 1), "Grundsteuer", 300m, AnlageVSource.Receipt),
        };

        var result = AnlageVCalculator.Calculate(postings, new Dictionary<AnlageVCategory, int> { [AnlageVCategory.BaseRent] = 9 });

        var rent = result.Income.Single(l => l.Info.Category == AnlageVCategory.BaseRent);
        Assert.Equal(9, rent.LineNumber);
        Assert.Equal(1240m, rent.Amount);
        Assert.Equal(new DateOnly(2026, 1, 1), rent.Postings[0].Date);
        Assert.Equal(AnlageVCategories.Info(AnlageVCategory.PropertyTax).DefaultLine, result.Expenses.Single(l => l.Info.Category == AnlageVCategory.PropertyTax).LineNumber);
        Assert.Equal(940m, result.Surplus);
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(60, true)]
    [InlineData(66, false)]
    public void Reduced_rent_hint_below_66_percent(int percent, bool hint) =>
        Assert.Equal(hint, AnlageVCalculator.ReducedRentHint(percent) is not null);

    [Fact]
    public void No_rent_percentage_gives_no_hint() => Assert.Null(AnlageVCalculator.ReducedRentHint(null));

    [Theory]
    [InlineData(40, false, 0.4)]
    [InlineData(40, true, 0.4)] // below 50 % the forecast does not help
    [InlineData(60, false, 0.6)]
    [InlineData(60, true, null)]
    [InlineData(66, false, null)]
    [InlineData(100, false, null)]
    public void Deductible_share_depends_on_rent_percentage_and_forecast(int percent, bool forecast, double? expected) =>
        Assert.Equal(expected is { } share ? Dec.From(share) : (decimal?)null, AnlageVCalculator.DeductibleShare(percent, forecast));

    [Fact]
    public void Expenses_are_reduced_proportionally_for_a_cheap_letting()
    {
        var postings = new[]
        {
            new AnlageVPosting(AnlageVCategory.BaseRent, new DateOnly(2026, 1, 1), "Miete", 5_000m, AnlageVSource.Payment),
            new AnlageVPosting(AnlageVCategory.PropertyTax, new DateOnly(2026, 3, 1), "Grundsteuer", 1_000m, AnlageVSource.Receipt),
        };

        var result = AnlageVCalculator.Calculate(postings, new Dictionary<AnlageVCategory, int>(), AnlageVCalculator.DeductibleShare(40, false));

        Assert.Equal(600m, result.Reduction);
        Assert.Equal(400m, result.DeductibleExpenses);
        Assert.Equal(4_600m, result.Surplus);
        Assert.Equal(1_000m, result.ExpenseTotal); // the lines still show the full amounts
    }
}
