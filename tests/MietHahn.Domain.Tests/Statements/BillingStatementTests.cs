using MietHahn.Domain.Common;
using MietHahn.Domain.Statements;

namespace MietHahn.Domain.Tests.Statements;

public class BillingStatementTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 20, 10, 0, 0, TimeSpan.Zero);

    private static BillingStatement Create(Period? period = null, int revision = 1, Guid? supersedes = null, string snapshot = "{}") =>
        new(Guid.NewGuid(), period ?? Period.CalendarYear(2026), revision, supersedes, snapshot, 1000m, 400m, Now, "  Erstfassung ");

    [Fact]
    public void First_revision_has_no_predecessor_and_keeps_the_trimmed_note()
    {
        var statement = Create();

        Assert.Equal(1, statement.Revision);
        Assert.Null(statement.SupersedesId);
        Assert.Equal("Erstfassung", statement.Note);
    }

    [Fact]
    public void Later_revisions_need_a_predecessor_and_the_first_must_not_have_one()
    {
        Assert.Throws<RuleViolationException>(() => Create(revision: 2));
        Assert.Throws<RuleViolationException>(() => Create(supersedes: Guid.NewGuid()));
        Assert.Equal(2, Create(revision: 2, supersedes: Guid.NewGuid()).Revision);
    }

    [Fact]
    public void Period_must_be_closed_and_at_most_twelve_months()
    {
        Assert.Throws<RuleViolationException>(() => Create(new Period(new DateOnly(2026, 1, 1))));
        Assert.Throws<RuleViolationException>(() => Create(new Period(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))));
        Assert.Equal(new DateOnly(2027, 6, 30), Create(new Period(new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30))).BillingPeriod.To);
    }

    [Fact]
    public void Snapshot_is_required()
    {
        Assert.Throws<ArgumentException>(() => Create(snapshot: " "));
    }
}
