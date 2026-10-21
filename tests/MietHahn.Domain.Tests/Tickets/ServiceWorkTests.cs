using MietHahn.Domain.Common;
using MietHahn.Domain.Tickets;

namespace MietHahn.Domain.Tests.Tickets;

public class ServiceWorkTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void Link_is_valid_until_it_expires_or_is_revoked()
    {
        var start = new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);
        var link = new TicketAccessLink(Guid.NewGuid(), TicketAccessLink.Hash("geheim"), "Firma Müller", start.AddDays(7));

        Assert.True(link.IsValid(start));
        Assert.False(link.IsValid(start.AddDays(7)));

        link.Revoke(start.AddDays(1));
        Assert.False(link.IsValid(start.AddDays(2)));
    }

    [Fact]
    public void Tokens_are_unguessable_and_only_their_hash_is_compared()
    {
        var first = TicketAccessLink.NewToken();
        var second = TicketAccessLink.NewToken();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('+', first);
        Assert.Equal(TicketAccessLink.Hash(first), TicketAccessLink.Hash(first));
        Assert.NotEqual(first, TicketAccessLink.Hash(first));
    }

    [Fact]
    public void Work_needs_content_and_a_sensible_duration()
    {
        Assert.Throws<RuleViolationException>(() => new TicketWork(Guid.NewGuid(), Day, 0, null, " ", null, "Hans"));
        Assert.Throws<RuleViolationException>(() => new TicketWork(Guid.NewGuid(), Day, 24 * 60 + 1, "x", null, null, "Hans"));
        Assert.Equal(45, new TicketWork(Guid.NewGuid(), Day, 45, null, null, null, "Hans").Minutes);
    }

    [Theory]
    [InlineData(CareInterval.Daily, 1)]
    [InlineData(CareInterval.Weekly, 7)]
    [InlineData(CareInterval.Monthly, 31)]
    public void Routine_is_due_again_after_its_interval(CareInterval interval, int days)
    {
        var routine = new CareRoutine(Guid.NewGuid(), "Treppenhaus", interval, null);

        Assert.True(routine.IsDue(null, Day));
        Assert.False(routine.IsDue(Day, Day));
        Assert.True(routine.IsDue(Day, Day.AddDays(days)));
    }

    [Fact]
    public void Routine_is_overdue_only_after_the_due_day_and_a_new_one_counts_from_creation()
    {
        var routine = new CareRoutine(Guid.NewGuid(), "Treppenhaus", CareInterval.Weekly, null);

        Assert.False(routine.IsOverdue(Day, Day, Day.AddDays(7)));
        Assert.True(routine.IsOverdue(Day, Day, Day.AddDays(8)));
        Assert.False(routine.IsOverdue(null, Day, Day.AddDays(3)));
        Assert.True(routine.IsOverdue(null, Day, Day.AddDays(8)));
        Assert.False(routine.IsOverdue(Day.AddDays(8), Day, Day.AddDays(9)));
    }
}
