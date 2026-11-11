using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Tests.Leases;

public class RentModelTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 0, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(650, 117.4, 121.7, 673.81)]
    [InlineData(650, 121.7, 117.4, 627.03)] // index dropped: rent drops
    [InlineData(10, 100, 100.05, 10.01)] // 10.005 → rounded up (commercial rounding)
    [InlineData(800, 110.0, 110.0, 800)]
    public void Indexed_rent_new_rent(double rent, double old, double newItem, double expected) =>
        Assert.Equal(Dec.From(expected), IndexedRent.NewRent(Dec.From(rent), Dec.From(old), Dec.From(newItem)));

    [Fact]
    public void Index_change_in_percent() => Assert.Equal(3.7m, IndexedRent.ChangePercent(117.4m, 121.7m));

    [Theory]
    [InlineData(2026, 10, 15, 2026, 12, 1)]
    [InlineData(2026, 12, 31, 2027, 2, 1)]
    public void Index_adjustment_takes_effect_from_the_month_after_next(int j, int m, int t, int ej, int em, int et) =>
        Assert.Equal(new DateOnly(ej, em, et), IndexedRent.EffectiveFrom(new DateOnly(j, m, t)));

    [Fact]
    public void Index_adjustment_earlier_than_12_months_is_rejected()
    {
        var lease = Lease();
        lease.SetRentModel(RentModel.Index, new DateOnly(2025, 11, 1), 117.4m);

        var errors = Assert.Throws<RuleViolationException>(() => lease.AdjustIndex(new DateOnly(2026, 12, 1), new DateOnly(2026, 9, 1), 121.7m));
        Assert.Contains("frühestens ab 01.01.2027", errors.Message, StringComparison.Ordinal);

        var newItem = lease.AdjustIndex(new DateOnly(2027, 1, 1), new DateOnly(2026, 9, 1), 121.7m);

        Assert.Equal(673.81m, newItem);
        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2026, 12, 31)));
        Assert.Equal(673.81m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2027, 1, 1)));
        Assert.Equal(new DateOnly(2026, 9, 1), lease.IndexBaseMonth);
        Assert.Equal(121.7m, lease.IndexBaseValue);
        Assert.Equal(new DateOnly(2027, 1, 1), lease.LastBaseRentChange());

        // next adjustment only one year later
        Assert.Throws<RuleViolationException>(() => lease.AdjustIndex(new DateOnly(2027, 6, 1), new DateOnly(2027, 3, 1), 123m));
    }

    [Fact]
    public void Indexed_rent_requires_a_base_index()
    {
        var lease = Lease();

        Assert.Throws<RuleViolationException>(() => lease.SetRentModel(RentModel.Index));
        Assert.Throws<RuleViolationException>(() => lease.AdjustIndex(new DateOnly(2027, 1, 1), new DateOnly(2026, 9, 1), 121.7m));
    }

    [Fact]
    public void Rent_steps_must_be_at_least_one_year_apart()
    {
        var lease = Lease();
        lease.SetRentModel(RentModel.Graduated);

        Assert.Throws<RuleViolationException>(() => lease.AddRentStep(new DateOnly(2026, 7, 1), 680m)); // < 1 year after start
        lease.AddRentStep(new DateOnly(2027, 1, 1), 680m);
        Assert.Throws<RuleViolationException>(() => lease.AddRentStep(new DateOnly(2027, 6, 1), 700m));
        lease.AddRentStep(new DateOnly(2028, 1, 1), 710m);
        Assert.Throws<RuleViolationException>(() => lease.AddRentStep(new DateOnly(2028, 1, 1), 720m));

        Assert.Equal(2, lease.RentSteps.Count);
    }

    [Fact]
    public void Rent_step_on_January_1_applies_from_January_1()
    {
        var lease = Lease();
        lease.SetRentModel(RentModel.Graduated);
        lease.AddRentStep(new DateOnly(2027, 1, 1), 680m);
        lease.AddRentStep(new DateOnly(2028, 1, 1), 710m);

        Assert.Empty(lease.ApplyDueSteps(new DateOnly(2026, 12, 31), Now));
        var applied = lease.ApplyDueSteps(new DateOnly(2027, 1, 1), Now);

        Assert.Equal(new DateOnly(2027, 1, 1), Assert.Single(applied).From);
        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2026, 12, 31)));
        Assert.Equal(680m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2027, 1, 1)));
        Assert.Empty(lease.ApplyDueSteps(new DateOnly(2027, 1, 2), Now)); // only once
        Assert.Throws<RuleViolationException>(() => lease.RemoveRentStep(applied[0].Id));
        Assert.Throws<RuleViolationException>(() => lease.SetRentModel(RentModel.Standard)); // pending step in 2028
    }

    [Fact]
    public void Rent_steps_only_for_graduated_rent() =>
        Assert.Throws<RuleViolationException>(() => Lease().AddRentStep(new DateOnly(2027, 1, 1), 680m));

    [Fact]
    public void Last_increase_for_the_standard_rent()
    {
        var lease = Lease();
        Assert.Null(lease.LastBaseRentChange());

        lease.SetComponent(RentComponentType.BaseRent, new DateOnly(2027, 4, 1), 700m);

        Assert.Equal(new DateOnly(2027, 4, 1), lease.LastBaseRentChange());
        Assert.Null(lease.LastBaseRentChange(new DateOnly(2027, 3, 31)));
    }

    private static Lease Lease()
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Start);
        lease.AddParty(Guid.CreateVersion7(), PartyRole.Tenant);
        lease.SetComponent(RentComponentType.BaseRent, Start, 650m);
        return lease;
    }
}
