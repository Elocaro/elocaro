using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Tests.Finance;

public class ChargeCalculationTests
{
    [Fact]
    public void Move_in_on_March_15_charges_17_31_of_the_rent()
    {
        var lease = Lease(new DateOnly(2026, 3, 15));

        var march = ChargeCalculation.Month(lease, 2026, 3);

        Assert.Equal([340.00m, 82.26m], march.Select(p => p.Amount));
        Assert.Equal("Kaltmiete 03/2026 (anteilig 17/31)", march[0].Name);
        Assert.All(march, p => Assert.True(p.ProRata));
        Assert.Equal([620m, 150m], ChargeCalculation.Month(lease, 2026, 4).Select(p => p.Amount));
        Assert.Empty(ChargeCalculation.Month(lease, 2026, 2));
    }

    [Fact]
    public void Move_out_and_rent_increase_in_the_same_month_are_prorated_by_day()
    {
        var lease = Lease(new DateOnly(2026, 1, 1));
        lease.SetComponent(RentComponentType.BaseRent, new DateOnly(2026, 6, 16), 680m);
        lease.Terminate(new DateOnly(2026, 4, 2), TerminatingParty.Tenant, new DateOnly(2026, 7, 10));

        var june = ChargeCalculation.Month(lease, 2026, 6);
        var july = ChargeCalculation.Month(lease, 2026, 7);

        // 15 days at 620 € + 15 days at 680 € in June (30 days)
        Assert.Equal(650m, june.Single(p => p.Component == RentComponentType.BaseRent).Amount);
        Assert.Equal("Kaltmiete 06/2026", june[0].Name);
        Assert.True(june[0].ProRata);
        Assert.Equal(decimal.Round(680m * 10 / 31, 2), july[0].Amount);
        Assert.Empty(ChargeCalculation.Month(lease, 2026, 8));
    }

    [Fact]
    public void Nothing_is_charged_before_accounting_start()
    {
        var lease = Lease(new DateOnly(2015, 5, 1));

        Assert.Empty(ChargeCalculation.Month(lease, 2026, 9, new DateOnly(2026, 10, 1)));
        Assert.Equal(770m, ChargeCalculation.Month(lease, 2026, 10, new DateOnly(2026, 10, 1)).Sum(p => p.Amount));
        Assert.Equal(
            [new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)],
            ChargeCalculation.Months(lease, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Months_end_with_the_lease()
    {
        var lease = Lease(new DateOnly(2026, 3, 15));
        lease.Terminate(new DateOnly(2026, 3, 20), TerminatingParty.Tenant, new DateOnly(2026, 5, 2));

        Assert.Equal(
            [new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1)],
            ChargeCalculation.Months(lease, new DateOnly(2020, 1, 1), new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Refund_is_negative_and_monthly_rents_cannot_be_edited_manually()
    {
        Assert.Throws<RuleViolationException>(() => new Charge(Guid.CreateVersion7(), ChargeType.Refund, new DateOnly(2026, 6, 1), 50m, "Guthaben NK 2025"));
        Assert.Throws<RuleViolationException>(() => new Charge(Guid.CreateVersion7(), ChargeType.DunningFee, new DateOnly(2026, 6, 1), 2.505m, "Mahngebühr"));

        var rent = Charge.MonthlyRent(Guid.CreateVersion7(), new DateOnly(2026, 6, 1), RentComponentType.BaseRent, new DateOnly(2026, 6, 3), 620m, "Kaltmiete 06/2026");
        Assert.Throws<RuleViolationException>(() => rent.Update(ChargeType.Miscellaneous, new DateOnly(2026, 6, 3), 600m, "x"));
        Assert.False(rent.Adjust(new DateOnly(2026, 6, 3), 620m, "Kaltmiete 06/2026"));
        Assert.True(rent.Adjust(new DateOnly(2026, 6, 3), 650m, "Kaltmiete 06/2026"));
        Assert.Equal(AllocationRank.Costs, new Charge(Guid.CreateVersion7(), ChargeType.DunningFee, new DateOnly(2026, 6, 1), 5m, "Mahngebühr").Rank);
    }

    private static Lease Lease(DateOnly start)
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, start);
        lease.AddParty(Guid.CreateVersion7(), PartyRole.Tenant);
        lease.SetComponent(RentComponentType.BaseRent, start, 620m);
        lease.SetComponent(RentComponentType.OperatingCostPrepayment, start, 150m);
        return lease;
    }
}
