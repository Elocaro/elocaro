using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;

namespace Elocaro.Domain.Tests.Properties;

public class PropertyOwnerTests
{
    private static readonly Guid House = Guid.NewGuid();

    private static PropertyOwner Owner(decimal share, DateOnly? from = null, DateOnly? to = null, Guid? person = null) =>
        new(House, person ?? Guid.NewGuid(), share, from, to);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.01)]
    [InlineData(33.333)]
    public void Impossible_shares_are_rejected(double share) =>
        Assert.Throws<RuleViolationException>(() => Owner((decimal)share));

    [Fact]
    public void Period_must_not_end_before_it_starts() =>
        Assert.Throws<RuleViolationException>(() => Owner(50m, new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30)));

    [Fact]
    public void Validity_includes_both_ends()
    {
        var owner = Owner(50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.False(owner.IsValidOn(new DateOnly(2025, 12, 31)));
        Assert.True(owner.IsValidOn(new DateOnly(2026, 1, 1)));
        Assert.True(owner.IsValidOn(new DateOnly(2026, 12, 31)));
        Assert.False(owner.IsValidOn(new DateOnly(2027, 1, 1)));
        Assert.True(Owner(50m).IsValidOn(new DateOnly(1999, 1, 1)));
    }

    [Fact]
    public void Shares_may_add_up_to_exactly_100_but_not_more()
    {
        var existing = new[] { Owner(60m) };

        Assert.False(PropertyOwner.ExceedsFullShare(existing, Owner(40m)));
        Assert.True(PropertyOwner.ExceedsFullShare(existing, Owner(40.01m)));
    }

    [Fact]
    public void Shares_are_checked_on_the_days_a_share_changes_inside_the_period()
    {
        // Two owners with 50 % each until mid 2026; one of them is replaced with 50 % from July.
        var existing = new[]
        {
            Owner(50m),
            Owner(50m, null, new DateOnly(2026, 6, 30)),
            Owner(30m, new DateOnly(2026, 7, 1)),
        };

        Assert.False(PropertyOwner.ExceedsFullShare(existing, Owner(20m, new DateOnly(2026, 7, 1))));
        Assert.True(PropertyOwner.ExceedsFullShare(existing, Owner(25m, new DateOnly(2026, 7, 1))));
        Assert.True(PropertyOwner.ExceedsFullShare(existing, Owner(5m, new DateOnly(2026, 1, 1))));
    }

    [Fact]
    public void The_same_person_cannot_own_twice_in_an_overlapping_period()
    {
        var person = Guid.NewGuid();
        var first = Owner(50m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), person);

        Assert.True(first.Overlaps(Owner(10m, new DateOnly(2026, 6, 30), null, person)));
        Assert.False(first.Overlaps(Owner(10m, new DateOnly(2026, 7, 1), null, person)));
        Assert.False(first.Overlaps(Owner(10m, new DateOnly(2026, 1, 1), null)));
    }

    [Fact]
    public void Ending_cannot_precede_the_start()
    {
        var owner = Owner(50m, new DateOnly(2026, 5, 1));

        Assert.Throws<RuleViolationException>(() => owner.End(new DateOnly(2026, 4, 30)));
        owner.End(new DateOnly(2026, 8, 31));
        Assert.Equal(new DateOnly(2026, 8, 31), owner.ValidTo);
    }

    [Fact]
    public void Year_share_is_weighted_by_days_and_zero_without_ownership()
    {
        var anna = Guid.NewGuid();
        var owners = new[]
        {
            Owner(50m, null, new DateOnly(2026, 6, 30), anna),
            Owner(100m, new DateOnly(2026, 7, 1), null, anna),
            Owner(50m),
        };

        Assert.Equal(50m, PropertyOwner.YearShare(owners, anna, 2025));
        Assert.Equal(Math.Round((50m * 181 + 100m * 184) / 365m, 4), PropertyOwner.YearShare(owners, anna, 2026));
        Assert.Equal(100m, PropertyOwner.YearShare(owners, anna, 2027));
        Assert.Equal(0m, PropertyOwner.YearShare(owners, Guid.NewGuid(), 2026));
    }
}
