using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Statements;

namespace MietHahn.Domain.Tests.Statements;

public class StatementDispatchTests
{
    private static StatementDispatch Create(DispatchChannel channel = DispatchChannel.Post, Guid? mailId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), channel, new DateOnly(2027, 2, 10), Guid.NewGuid(), null, mailId, 1200m, 1200m, null, "  ");

    [Fact]
    public void Email_dispatch_needs_the_outgoing_mail_as_proof()
    {
        Assert.Throws<RuleViolationException>(() => Create(DispatchChannel.Email));
        Assert.Equal(DispatchChannel.Email, Create(DispatchChannel.Email, Guid.NewGuid()).Channel);
    }

    [Fact]
    public void Receipt_cannot_precede_the_dispatch()
    {
        var dispatch = Create();

        Assert.Throws<RuleViolationException>(() => dispatch.ConfirmReceipt(new DateOnly(2027, 2, 9), null));
        dispatch.ConfirmReceipt(new DateOnly(2027, 2, 12), " Einwurf-Einschreiben ");

        Assert.Equal(new DateOnly(2027, 2, 12), dispatch.ReceivedOn);
        Assert.Equal("Einwurf-Einschreiben", dispatch.ReceiptNote);
        Assert.Null(dispatch.Note);
    }
}

public class PrepaymentProposalTests
{
    private static PrepaymentProposal Create(RentComponentType component = RentComponentType.OperatingCostPrepayment, decimal proposed = 105m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), component, 100m, proposed, new DateOnly(2027, 4, 1));

    [Fact]
    public void Only_operating_and_heating_prepayments_can_be_adjusted()
    {
        Assert.Throws<RuleViolationException>(() => Create(RentComponentType.BaseRent));
        Assert.Throws<RuleViolationException>(() => Create(proposed: -1m));
        Assert.Equal(ProposalStatus.Open, Create(RentComponentType.HeatingCostPrepayment).Status);
    }

    [Fact]
    public void Proposal_is_decided_once()
    {
        var applied = Create();
        applied.Apply(new DateOnly(2027, 3, 1));

        Assert.Equal((ProposalStatus.Applied, new DateOnly(2027, 3, 1)), (applied.Status, applied.DecidedOn!.Value));
        Assert.Throws<RuleViolationException>(() => applied.Apply(new DateOnly(2027, 3, 2)));
        Assert.Throws<RuleViolationException>(() => applied.Decline(new DateOnly(2027, 3, 2)));

        var declined = Create();
        declined.Decline(new DateOnly(2027, 3, 1));
        Assert.Equal(ProposalStatus.Declined, declined.Status);
    }
}

public class PrepaymentCalculatorTests
{
    [Theory]
    [InlineData(1200, 365, 365, 5, 105)] // 100.00 × 1.05 = 105.00
    [InlineData(1200, 365, 365, 0, 100)]
    [InlineData(1000, 365, 365, 5, 88)] // 83.33 × 1.05 = 87.50 → 88
    [InlineData(600, 182, 365, 0, 101)] // half a year extrapolated: 600 ÷ 182 × 365 = 1,203.30 ÷ 12 = 100.27 → 101
    [InlineData(0, 365, 365, 5, 0)]
    [InlineData(-120, 365, 365, 0, 0)] // credit larger than the costs never gives a negative prepayment
    public void Prepayment_is_a_twelfth_of_the_annual_costs_plus_margin_rounded_up(decimal share, int tenantDays, int periodDays, decimal percent, decimal expected)
    {
        Assert.Equal(expected, PrepaymentCalculator.Propose(share, tenantDays, periodDays, percent));
    }

    [Fact]
    public void No_tenancy_days_give_no_proposal()
    {
        Assert.Equal(0m, PrepaymentCalculator.Propose(1200m, 0, 365, 5m));
    }

    [Theory]
    [InlineData(2027, 2, 10, 2027, 4, 1)]
    [InlineData(2027, 11, 30, 2028, 1, 1)]
    [InlineData(2027, 12, 1, 2028, 2, 1)]
    public void Effective_date_is_the_first_of_the_month_after_next(int y, int m, int d, int ey, int em, int ed)
    {
        Assert.Equal(new DateOnly(ey, em, ed), PrepaymentCalculator.DefaultEffectiveDate(new DateOnly(y, m, d)));
    }
}
