using Elocaro.Domain.Finance;

namespace Elocaro.Domain.Tests.Finance;

public class AllocationTests
{
    private static readonly Guid BaseRentMarch = Id(1);
    private static readonly Guid OcMarch = Id(2);
    private static readonly Guid BaseRentApril = Id(3);
    private static readonly Guid OcApril = Id(4);
    private static readonly Guid DunningFee = Id(5);

    private static readonly AllocationItem[] Rents =
    [
        Rent(BaseRentMarch, 3, 600m),
        Rent(OcMarch, 3, 200m),
        Rent(BaseRentApril, 4, 600m),
        Rent(OcApril, 4, 200m),
    ];

    [Fact]
    public void Partial_payment_settles_oldest_monthly_rent_pro_rata_per_component()
    {
        var result = Allocation.Distribute(Rents, [Payment(10, 4, 10, 1000m)], AllocationOrder.Statutory);

        Assert.Equal(600m, result.Settled(BaseRentMarch));
        Assert.Equal(200m, result.Settled(OcMarch));
        Assert.Equal(150m, result.Settled(BaseRentApril));
        Assert.Equal(50m, result.Settled(OcApril));
        Assert.Equal(600m, result.Open.Values.Sum());
        Assert.Equal(0m, result.Overpayment);
    }

    [Fact]
    public void Costs_before_interest_before_principal_only_with_statutory_order()
    {
        var claims = Rents.Append(new AllocationItem(DunningFee, new DateOnly(2026, 4, 20), 5m, AllocationRank.Costs)).ToList();
        var payment = Payment(10, 4, 25, 805m);

        var statutory = Allocation.Distribute(claims, [payment], AllocationOrder.Statutory);
        Assert.Equal(5m, statutory.Settled(DunningFee));
        Assert.Equal(800m, statutory.Settled(BaseRentMarch) + statutory.Settled(OcMarch));

        var rentFirst = Allocation.Distribute(claims, [payment], AllocationOrder.RentFirst);
        Assert.Equal(0m, rentFirst.Settled(DunningFee));
        Assert.Equal(5m, rentFirst.Settled(BaseRentApril) + rentFirst.Settled(OcApril));

        var oldest = Allocation.Distribute(claims, [payment], AllocationOrder.OldestFirst);
        Assert.Equal(0m, oldest.Settled(DunningFee));
    }

    [Fact]
    public void Tenant_payment_designation_takes_precedence()
    {
        var payment = new AllocationItem(Id(10), new DateOnly(2026, 4, 10), 800m, Designation: BaseRentApril);

        var result = Allocation.Distribute(Rents, [payment], AllocationOrder.Statutory);

        Assert.Equal(600m, result.Settled(BaseRentApril));
        Assert.Equal(150m, result.Settled(BaseRentMarch));
        Assert.Equal(50m, result.Settled(OcMarch));
    }

    [Fact]
    public void Prepayment_settles_only_when_due_with_payment_date_as_inflow()
    {
        // March rent already paid on 28 Feb, April rent only on 2 May
        var result = Allocation.Distribute(Rents, [Payment(10, 2, 28, 800m), Payment(11, 5, 2, 800m)], AllocationOrder.Statutory);

        Assert.All(result.Shares.Where(a => a.ClaimId == BaseRentMarch || a.ClaimId == OcMarch), a => Assert.Equal(new DateOnly(2026, 2, 28), a.Date));
        Assert.All(result.Shares.Where(a => a.ClaimId == BaseRentApril), a => Assert.Equal(new DateOnly(2026, 5, 2), a.Date));
        Assert.All(result.Open.Values, o => Assert.Equal(0m, o));
    }

    [Fact]
    public void Overpayment_remains_as_credit_and_settles_later_claims()
    {
        var result = Allocation.Distribute(Rents.Take(2), [Payment(10, 3, 3, 1000m)], AllocationOrder.Statutory);
        Assert.Equal(200m, result.Overpayment);

        var later = Allocation.Distribute(Rents, [Payment(10, 3, 3, 1000m)], AllocationOrder.Statutory);
        Assert.Equal(0m, later.Overpayment);
        Assert.Equal(150m, later.Settled(BaseRentApril));
        Assert.All(later.Shares, a => Assert.Equal(new DateOnly(2026, 3, 3), a.Date));
    }

    [Fact]
    public void Rounding_cents_go_to_the_largest_remainders()
    {
        AllocationItem[] threeParts = [Rent(Id(1), 3, 100m), Rent(Id(2), 3, 100m), Rent(Id(3), 3, 100m)];

        var result = Allocation.Distribute(threeParts, [Payment(10, 3, 5, 100m)], AllocationOrder.Statutory);

        Assert.Equal(100m, result.Shares.Sum(a => a.Amount));
        Assert.Equal([33.34m, 33.33m, 33.33m], threeParts.Select(f => result.Settled(f.Id)));
    }

    private static AllocationItem Rent(Guid id, int month, decimal amount) => new(id, new DateOnly(2026, month, 3), amount, IsRent: true);

    private static AllocationItem Payment(int id, int month, int day, decimal amount) => new(Id(id), new DateOnly(2026, month, day), amount);

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");
}
