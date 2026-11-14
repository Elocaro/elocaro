using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;

namespace Elocaro.Domain.Tests.Finance;

public class RentReductionTests
{
    private static readonly Guid Lease = Guid.NewGuid();
    private static readonly DateOnly April = new(2026, 4, 1);

    private static Period WholeMonth(int month) => Period.Month(2026, month);

    private static RentReduction Percent(decimal percent, DateOnly from, DateOnly? to = null, RentReductionStatus status = RentReductionStatus.Acknowledged) =>
        new(Lease, "Heizung ausgefallen", from, to, percent, null, status);

    [Fact]
    public void Percent_applies_to_the_gross_rent_of_the_month()
    {
        var reduction = Percent(20m, April);

        Assert.Equal(154.00m, reduction.AmountFor(April, 770m, WholeMonth(4)));
    }

    [Fact]
    public void Partial_month_is_prorated_by_day_and_rounded_once()
    {
        // 11 of 30 days of April: 770 * 20 % * 11 / 30 = 56.4666... -> 56.47
        var reduction = Percent(20m, new DateOnly(2026, 4, 20));

        Assert.Equal(56.47m, reduction.AmountFor(April, 770m, WholeMonth(4)));
    }

    [Fact]
    public void Reduction_covers_only_the_days_the_lease_runs_in_a_short_month()
    {
        // Move-in on 15 April: rent for 16 days (410), reduction for the whole of the rent period = 20 % of what is charged.
        var reduction = Percent(20m, April);
        var lease = new Period(new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 30));

        Assert.Equal(82.00m, reduction.AmountFor(April, 410m, lease));
    }

    [Fact]
    public void Fixed_amount_is_a_full_monthly_amount_and_never_exceeds_the_rent()
    {
        var reduction = new RentReduction(Lease, "Lärm", April, null, null, 100m, RentReductionStatus.Acknowledged);

        Assert.Equal(100m, reduction.AmountFor(April, 770m, WholeMonth(4)));
        Assert.Equal(60m, reduction.AmountFor(April, 60m, WholeMonth(4)));
        Assert.Equal(50m, new RentReduction(Lease, "Lärm", new DateOnly(2026, 4, 16), null, null, 100m, RentReductionStatus.Acknowledged).AmountFor(April, 770m, WholeMonth(4)));
    }

    [Fact]
    public void Months_outside_the_period_have_no_reduction()
    {
        var reduction = Percent(20m, new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 20));

        Assert.Equal(0m, reduction.AmountFor(new DateOnly(2026, 3, 1), 770m, WholeMonth(3)));
        Assert.Equal(0m, reduction.AmountFor(new DateOnly(2026, 5, 1), 770m, WholeMonth(5)));
        Assert.Equal(56.47m, reduction.AmountFor(April, 770m, WholeMonth(4)));
    }

    [Fact]
    public void Only_acknowledged_reductions_are_granted_and_an_ended_one_only_if_it_was_acknowledged()
    {
        Assert.False(Percent(20m, April, status: RentReductionStatus.Announced).Grants);
        Assert.True(Percent(20m, April).Grants);
        Assert.False(Percent(20m, April, status: RentReductionStatus.Disputed).Grants);
        Assert.True(Percent(20m, April, status: RentReductionStatus.Disputed).IsDisputed);

        var ended = Percent(20m, April, new DateOnly(2026, 6, 30), RentReductionStatus.Disputed);
        ended.Apply("Heizung ausgefallen", April, new DateOnly(2026, 6, 30), 20m, null, RentReductionStatus.Ended);
        Assert.False(ended.Grants);

        var acknowledged = Percent(20m, April);
        acknowledged.Apply("Heizung ausgefallen", April, new DateOnly(2026, 6, 30), 20m, null, RentReductionStatus.Ended);
        Assert.True(acknowledged.Grants);
    }

    [Fact]
    public void Disputing_again_withdraws_the_acknowledgement()
    {
        var reduction = Percent(20m, April);
        reduction.Apply("Heizung ausgefallen", April, null, 20m, null, RentReductionStatus.Disputed);

        Assert.Null(reduction.AcknowledgedOn);
        Assert.False(reduction.Grants);
    }

    [Theory]
    [InlineData(0.0, null)]
    [InlineData(100.5, null)]
    [InlineData(null, 0.0)]
    [InlineData(null, null)]
    [InlineData(20.0, 100.0)]
    public void Invalid_quota_or_amount_is_rejected(double? percent, double? amount)
    {
        Assert.Throws<RuleViolationException>(() => new RentReduction(Lease, "Mangel", April, null, (decimal?)percent, (decimal?)amount, RentReductionStatus.Announced));
    }

    [Fact]
    public void End_before_start_and_ended_without_end_date_are_rejected()
    {
        Assert.Throws<RuleViolationException>(() => Percent(20m, April, new DateOnly(2026, 3, 31)));
        Assert.Throws<RuleViolationException>(() => Percent(20m, April, null, RentReductionStatus.Ended));
    }

    [Fact]
    public void The_credit_is_negative_and_generated()
    {
        var credit = Charge.Reduction(Lease, Guid.NewGuid(), April, new DateOnly(2026, 4, 3), -154m, "Mietminderung 04/2026 (20 %)");

        Assert.Equal(ChargeType.RentReduction, credit.Kind);
        Assert.True(credit.Generated);
        Assert.False(credit.Automatic);
        Assert.Throws<RuleViolationException>(() => credit.Update(ChargeType.Miscellaneous, new DateOnly(2026, 4, 3), 10m, "x"));
        Assert.Throws<RuleViolationException>(() => Charge.Reduction(Lease, Guid.NewGuid(), April, new DateOnly(2026, 4, 3), 154m, "positiv"));
        Assert.Throws<ArgumentException>(() => new Charge(Lease, ChargeType.RentReduction, new DateOnly(2026, 4, 3), -1m, "von Hand"));
    }
}
