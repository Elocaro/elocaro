using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;

namespace MietHahn.Domain.Tests.Leases;

public class LeaseTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void Rent_increase_on_March_1_creates_new_component_old_rent_remains()
    {
        var lease = NewLease();

        lease.SetComponent(RentComponentType.BaseRent, new DateOnly(2026, 3, 1), 700m);

        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2026, 2, 28)));
        Assert.Equal(700m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2026, 3, 1)));
        var baseRents = lease.Components.Where(b => b.Kind == RentComponentType.BaseRent).OrderBy(b => b.Validity.From).ToList();
        Assert.Equal([new Period(Start, new DateOnly(2026, 2, 28)), new Period(new DateOnly(2026, 3, 1))], baseRents.Select(k => k.Validity));
        Assert.Equal(650m + 180m + 90m, lease.MonthlyRentOn(new DateOnly(2026, 2, 1)));
        Assert.Equal(700m + 180m + 90m, lease.MonthlyRentOn(new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void Changing_prepayment_does_not_affect_base_rent()
    {
        var lease = NewLease();

        lease.SetComponent(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 1, 1), 210m);

        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2027, 1, 1)));
        Assert.Equal(210m, lease.AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2027, 1, 1)));
        Assert.Equal(180m, lease.AmountAt(RentComponentType.OperatingCostPrepayment, new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Flat_rate_gets_no_operating_cost_statement()
    {
        var sharedFlat = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start) { BillingType = BillingType.FlatRate };

        Assert.False(sharedFlat.ReceivesOperatingCostStatement);
        Assert.False(sharedFlat.ReceivesHeatingCostStatement);
        sharedFlat.HeatingCostsConsumptionBased = true;
        Assert.True(sharedFlat.ReceivesHeatingCostStatement);
        Assert.True(NewLease().ReceivesOperatingCostStatement);
    }

    [Fact]
    public void Component_outside_the_term_is_rejected()
    {
        var lease = NewLease();

        var errors = Assert.Throws<RuleViolationException>(() => lease.SetComponent(RentComponentType.BaseRent, new DateOnly(2025, 12, 1), 600m));
        Assert.Contains("außerhalb der Vertragslaufzeit", errors.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 10, 5, 2026, 12, 31)] // Zugang am 3. Werktag → Ablauf des übernächsten Monats
    [InlineData(2026, 10, 6, 2027, 1, 31)] // einen Tag später → ein Monat mehr
    [InlineData(2026, 2, 3, 2026, 4, 30)]
    [InlineData(2026, 2, 28, 2026, 5, 31)]
    public void Termination_date_for_the_tenant(int j, int m, int t, int ej, int em, int et) =>
        Assert.Equal(new DateOnly(ej, em, et), Lease.TerminationDateForTenant(new DateOnly(j, m, t)));

    [Theory]
    [InlineData(2026, 3)]
    [InlineData(2021, 6)] // 5 Jahre
    [InlineData(2018, 9)] // 8 Jahre
    public void Landlord_notice_period_by_lease_duration(int startYear, int months)
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, new DateOnly(startYear, 1, 1));

        Assert.Equal(months, lease.LandlordNoticePeriodMonths(new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void Termination_ends_lease_and_components_revoking_reopens()
    {
        var lease = NewLease();
        var end = new DateOnly(2026, 12, 31);

        lease.Terminate(new DateOnly(2026, 10, 5), TerminatingParty.Tenant, end);

        Assert.Equal(end, lease.End);
        Assert.Equal(LeaseStatus.Terminated, lease.StatusAt(new DateOnly(2026, 11, 1)));
        Assert.Equal(LeaseStatus.Ended, lease.StatusAt(new DateOnly(2027, 1, 1)));
        Assert.All(lease.Components.Concat<ITimeDependent>(lease.People), b => Assert.Equal(end, b.Validity.To));
        Assert.Equal(0m, lease.MonthlyRentOn(new DateOnly(2027, 1, 1)));

        lease.RevokeTermination();

        Assert.Null(lease.End);
        Assert.Equal(LeaseStatus.Active, lease.StatusAt(new DateOnly(2027, 1, 1)));
        Assert.Equal(650m, lease.AmountAt(RentComponentType.BaseRent, new DateOnly(2027, 6, 1)));
        Assert.Equal(2, lease.OccupantCountOn(new DateOnly(2027, 6, 1)));
    }

    [Fact]
    public void Fixed_term_lease_ends_at_the_fixed_term()
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Start, new DateOnly(2027, 12, 31));

        Assert.Equal(new DateOnly(2027, 12, 31), lease.End);
        Assert.Equal(LeaseStatus.Terminated, lease.StatusAt(Start)); // Ende steht fest
        Assert.Equal(LeaseStatus.Future, lease.StatusAt(Start.AddDays(-1)));
        Assert.Throws<RuleViolationException>(() => new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Start, Start.AddDays(-1)));
    }

    [Fact]
    public void Multiple_tenants_and_guarantor_at_least_one_tenant_remains()
    {
        var lease = NewLease();
        var second = Guid.CreateVersion7();
        var guarantor = Guid.CreateVersion7();

        lease.AddParty(second, PartyRole.Tenant);
        lease.AddParty(guarantor, PartyRole.Guarantor);

        Assert.Equal(2, lease.Tenant.Count());
        Assert.Throws<RuleViolationException>(() => lease.AddParty(second, PartyRole.Guarantor));
        lease.RemoveParty(second);
        Assert.Throws<RuleViolationException>(() => lease.RemoveParty(lease.Tenant.Single().PersonId));
        lease.RemoveParty(guarantor);
    }

    [Fact]
    public void Occupant_count_is_time_dependent()
    {
        var lease = NewLease();

        lease.SetOccupantCount(new DateOnly(2026, 7, 1), 3);

        Assert.Equal(2, lease.OccupantCountOn(new DateOnly(2026, 6, 30)));
        Assert.Equal(3, lease.OccupantCountOn(new DateOnly(2026, 7, 1)));
        Assert.Throws<RuleViolationException>(() => lease.SetOccupantCount(new DateOnly(2026, 8, 1), 0));
    }

    [Fact]
    public void End_parking_space()
    {
        var lease = NewLease();
        lease.SetComponent(RentComponentType.ParkingSpace, Start, 40m);

        lease.EndComponent(RentComponentType.ParkingSpace, new DateOnly(2026, 6, 30));

        Assert.Equal(40m, lease.AmountAt(RentComponentType.ParkingSpace, new DateOnly(2026, 6, 30)));
        Assert.Null(lease.AmountAt(RentComponentType.ParkingSpace, new DateOnly(2026, 7, 1)));
    }

    private static Lease NewLease()
    {
        var lease = new Lease(Guid.CreateVersion7(), Guid.CreateVersion7(), null, Start);
        lease.AddParty(Guid.CreateVersion7(), PartyRole.Tenant);
        lease.SetComponent(RentComponentType.BaseRent, Start, 650m);
        lease.SetComponent(RentComponentType.OperatingCostPrepayment, Start, 180m);
        lease.SetComponent(RentComponentType.HeatingCostPrepayment, Start, 90m);
        lease.SetOccupantCount(Start, 2);
        return lease;
    }
}
