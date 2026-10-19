using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;

namespace MietHahn.Domain.Tests.Finance;

public class DunningTests
{
    private static readonly DateOnly Today = new(2026, 4, 30);

    private static readonly DunningRules Rules = DunningRules.Default with
    {
        FirstNoticeFee = 5m,
        SecondNoticeFee = 10m,
        ChargeInterest = true,
        BaseRate = 2.27m,
    };

    [Fact]
    public void Within_grace_period_there_is_no_arrears()
    {
        // 5 days overdue is still within the 5-day grace period.
        Assert.Null(DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-5), 800m)], []));
        Assert.NotNull(DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-6), 800m)], []));
    }

    [Fact]
    public void Below_the_minimum_amount_there_is_no_dunning()
    {
        Assert.Null(DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-20), 4.99m)], []));
        Assert.NotNull(DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-20), 5m)], []));
    }

    [Fact]
    public void First_dunning_is_a_reminder_without_interest_and_with_the_configured_fee()
    {
        var rules = Rules with { ReminderFee = 2m };

        var plan = DunningPlanner.Plan(rules, Today, [Rent(Today.AddDays(-10), 800m), Rent(Today.AddDays(-40), 100m, open: 50m)], [])!;

        Assert.Equal(DunningLevel.Reminder, plan.Level);
        Assert.Equal(850m, plan.Principal);
        Assert.Equal(2m, plan.Fee);
        Assert.Equal(0m, plan.Interest);
        Assert.Equal(Today.AddDays(-40), plan.OldestDue);
        Assert.Equal([Today.AddDays(-40), Today.AddDays(-10)], plan.Claims.Select(c => c.Due));
    }

    [Fact]
    public void Fees_and_interest_are_not_themselves_a_reason_to_dun()
    {
        var claims = new[]
        {
            Claim(ChargeType.DunningFee, Today.AddDays(-30), 5m),
            Claim(ChargeType.DefaultInterest, Today.AddDays(-30), 12m),
        };

        Assert.Null(DunningPlanner.Plan(Rules, Today, claims, []));
    }

    [Fact]
    public void Next_level_only_after_the_waiting_period_and_only_while_the_arrears_remain()
    {
        var claims = new[] { Rent(Today.AddDays(-40), 800m) };
        var reminded = new DunningHistoryEntry(DunningLevel.Reminder, DunningStatus.Issued, Today.AddDays(-13));

        Assert.Null(DunningPlanner.Plan(Rules, Today, claims, [reminded]));

        reminded = reminded with { Date = Today.AddDays(-14) };
        var first = DunningPlanner.Plan(Rules, Today, claims, [reminded])!;
        Assert.Equal(DunningLevel.FirstNotice, first.Level);
        Assert.Equal(5m, first.Fee);

        var second = DunningPlanner.Plan(Rules, Today, claims, [reminded, new DunningHistoryEntry(DunningLevel.FirstNotice, DunningStatus.Issued, Today.AddDays(-14))])!;
        Assert.Equal(DunningLevel.SecondNotice, second.Level);
        Assert.Equal(10m, second.Fee);
    }

    [Fact]
    public void After_the_second_notice_there_are_no_further_proposals()
    {
        var history = new[] { new DunningHistoryEntry(DunningLevel.SecondNotice, DunningStatus.Issued, Today.AddDays(-60)) };

        Assert.Null(DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-100), 800m)], history));
    }

    [Fact]
    public void New_arrears_after_an_earlier_dunning_start_again_with_the_reminder()
    {
        // The claim that was dunned has been paid; the new one is younger than the last dunning.
        var history = new[] { new DunningHistoryEntry(DunningLevel.SecondNotice, DunningStatus.Issued, Today.AddDays(-60)) };

        var plan = DunningPlanner.Plan(Rules, Today, [Rent(Today.AddDays(-10), 800m)], history)!;

        Assert.Equal(DunningLevel.Reminder, plan.Level);
    }

    [Fact]
    public void Dismissed_proposal_pauses_new_proposals_for_14_days()
    {
        var claims = new[] { Rent(Today.AddDays(-20), 800m) };

        Assert.Null(DunningPlanner.Plan(Rules, Today, claims, [new DunningHistoryEntry(DunningLevel.Reminder, DunningStatus.Dismissed, Today.AddDays(-13))]));
        Assert.NotNull(DunningPlanner.Plan(Rules, Today, claims, [new DunningHistoryEntry(DunningLevel.Reminder, DunningStatus.Dismissed, Today.AddDays(-14))]));
    }

    [Fact]
    public void Payment_removes_the_arrears_and_thus_the_proposal()
    {
        var unpaid = Rent(Today.AddDays(-20), 800m);

        Assert.NotNull(DunningPlanner.Plan(Rules, Today, [unpaid], []));
        Assert.Null(DunningPlanner.Plan(Rules, Today, [unpaid with { Open = 0m }], []));
    }

    [Fact]
    public void Interest_is_calculated_from_the_first_notice_on_with_base_rate_plus_5_points()
    {
        var claims = new[] { Rent(Today.AddDays(-73), 1000m) };
        var history = new[] { new DunningHistoryEntry(DunningLevel.Reminder, DunningStatus.Issued, Today.AddDays(-20)) };

        var plan = DunningPlanner.Plan(Rules, Today, claims, history)!;

        // 1000 € x 7.27 % x 73/365 = 14.54 €
        Assert.Equal(14.54m, plan.Interest);
        Assert.Equal(7.27m, Rules.InterestRate);
    }

    [Fact]
    public void Without_a_base_rate_or_with_interest_switched_off_no_interest_is_calculated()
    {
        var claims = new[] { Rent(Today.AddDays(-73), 1000m) };
        var history = new[] { new DunningHistoryEntry(DunningLevel.Reminder, DunningStatus.Issued, Today.AddDays(-20)) };

        Assert.Equal(0m, DunningPlanner.Plan(Rules with { BaseRate = null }, Today, claims, history)!.Interest);
        Assert.Equal(0m, DunningPlanner.Plan(Rules with { ChargeInterest = false }, Today, claims, history)!.Interest);
    }

    [Theory]
    [InlineData(1000, 365, 7.27, 72.70)]
    [InlineData(800, 30, 8.27, 5.44)]
    [InlineData(100, 1, 7.27, 0.02)]
    [InlineData(0, 30, 7.27, 0)]
    [InlineData(100, 0, 7.27, 0)]
    [InlineData(100, 30, 0, 0)]
    public void Default_interest_is_simple_interest_on_actual_over_365(double open, int days, double percent, double expected)
    {
        var due = new DateOnly(2026, 1, 10);

        Assert.Equal((decimal)expected, DefaultInterest.Calculate((decimal)open, due, due.AddDays(days), (decimal)percent));
    }

    [Fact]
    public void A_strongly_negative_base_rate_never_leads_to_a_negative_interest_rate()
    {
        var rules = Rules with { BaseRate = -6m };

        Assert.Equal(0m, rules.InterestRate);
    }

    [Theory]
    [InlineData(1000, new[] { 500 }, false, false)] // half a rent
    [InlineData(1000, new[] { 1000 }, false, false)] // exactly one rent in one term
    [InlineData(1000, new[] { 600, 600 }, true, false)] // two consecutive terms, more than one rent
    [InlineData(1000, new[] { 500, 500 }, false, false)] // two terms, but only one rent in total
    [InlineData(1000, new[] { 1000, 1000 }, true, true)] // two rents
    [InlineData(1000, new[] { 1000, 0, 1000 }, false, true)] // two rents but not consecutive
    [InlineData(1000, new[] { 700, 0, 700 }, false, false)] // gap in between, together 1400
    public void Termination_relevance_after_Section_543_para_2_no_3_BGB(int rent, int[] openPerMonth, bool consecutive, bool twoRents)
    {
        var items = openPerMonth.Select((open, i) => (Due: new DateOnly(2026, 1 + i, 3), Open: (decimal)open)).ToList();

        var result = TerminationRelevance.Check(rent, items);

        Assert.Equal(consecutive, result.TwoConsecutiveTerms);
        Assert.Equal(twoRents, result.TwoMonthlyRents);
        Assert.Equal(consecutive || twoRents, result.Any);
    }

    [Fact]
    public void Termination_relevance_needs_a_monthly_rent()
    {
        Assert.False(TerminationRelevance.Check(0m, [(Today, 5000m)]).Any);
    }

    [Fact]
    public void Proposal_can_be_adjusted_but_not_after_the_decision()
    {
        var dunning = new Dunning(Guid.NewGuid(), DunningLevel.Reminder, Today, 800m, 0m, 0m, Today.AddDays(-10), false);

        Assert.False(dunning.Update(DunningLevel.Reminder, 800m, 0m, 0m, Today.AddDays(-10), false));
        Assert.True(dunning.Update(DunningLevel.Reminder, 1600m, 0m, 0m, Today.AddDays(-10), true));
        Assert.Equal(1600m, dunning.Principal);

        dunning.Issue(Today, Today.AddDays(7), 1600m, 0m, 0m, Guid.NewGuid(), null, null, null, null);

        Assert.Equal(DunningStatus.Issued, dunning.Status);
        Assert.Equal(Today, dunning.DecidedOn);
        Assert.False(dunning.SentByMail);
        Assert.Throws<RuleViolationException>(() => dunning.Dismiss(Today));
        Assert.Throws<RuleViolationException>(() => dunning.Update(DunningLevel.FirstNotice, 1m, 0m, 0m, Today, false));
    }

    [Fact]
    public void Deadline_must_not_precede_the_letter_date()
    {
        var dunning = new Dunning(Guid.NewGuid(), DunningLevel.Reminder, Today, 800m, 0m, 0m, Today.AddDays(-10), false);

        Assert.Throws<RuleViolationException>(() => dunning.Issue(Today, Today.AddDays(-1), 800m, 0m, 0m, Guid.NewGuid(), null, null, null, null));
    }

    [Fact]
    public void Hold_is_active_until_its_last_day_and_can_be_ended()
    {
        var hold = new DunningHold(Guid.NewGuid(), "Ratenvereinbarung", Today.AddDays(10));

        Assert.True(hold.IsActiveOn(Today.AddDays(10)));
        Assert.False(hold.IsActiveOn(Today.AddDays(11)));

        hold.End(Today);

        Assert.False(hold.IsActiveOn(Today));
        Assert.True(hold.IsActiveOn(Today.AddDays(-1)));
    }

    [Fact]
    public void Open_ended_hold_applies_until_cancelled_and_needs_a_reason()
    {
        Assert.True(new DunningHold(Guid.NewGuid(), "Ratenvereinbarung", null).IsActiveOn(Today.AddYears(5)));
        Assert.Throws<RuleViolationException>(() => new DunningHold(Guid.NewGuid(), " ", null));
    }

    private static DunningClaim Rent(DateOnly due, decimal amount, decimal? open = null) =>
        new(Guid.NewGuid(), ChargeType.Rent, $"Miete {due:MM/yyyy}", due, amount, open ?? amount);

    private static DunningClaim Claim(ChargeType kind, DateOnly due, decimal amount) =>
        new(Guid.NewGuid(), kind, kind.ToString(), due, amount, amount);
}
