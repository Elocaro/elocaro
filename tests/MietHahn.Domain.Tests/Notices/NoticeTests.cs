using MietHahn.Domain.Common;
using MietHahn.Domain.Notices;

namespace MietHahn.Domain.Tests.Notices;

public class NoticeTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void Notice_is_current_within_its_validity_only()
    {
        var notice = new Notice("Wasser", "Abgestellt", Guid.NewGuid(), Day, Day.AddDays(2), false);

        Assert.False(notice.IsCurrent(Day.AddDays(-1)));
        Assert.True(notice.IsCurrent(Day));
        Assert.True(notice.IsCurrent(Day.AddDays(2)));
        Assert.False(notice.IsCurrent(Day.AddDays(3)));
    }

    [Fact]
    public void Open_ended_notice_stays_current_until_withdrawn()
    {
        var notice = new Notice("Hausordnung", "Text", Guid.NewGuid(), Day, null, true);
        Assert.True(notice.IsCurrent(Day.AddYears(5)));

        notice.Withdraw(Day.AddDays(4));

        Assert.True(notice.IsCurrent(Day.AddDays(4)));
        Assert.False(notice.IsCurrent(Day.AddDays(5)));
    }

    [Fact]
    public void Validity_end_before_start_and_empty_text_are_rejected()
    {
        Assert.Throws<RuleViolationException>(() => new Notice("A", "B", Guid.NewGuid(), Day, Day.AddDays(-1), false));
        Assert.Throws<RuleViolationException>(() => new Notice("A", " ", Guid.NewGuid(), Day, null, false));
    }

    [Fact]
    public void Thread_tracks_who_has_unread_messages()
    {
        var thread = new MessageThread(Guid.NewGuid(), "Schlüssel", DateTimeOffset.UnixEpoch);

        thread.Add(fromTenant: true, DateTimeOffset.UnixEpoch.AddHours(1));
        Assert.True(thread.AdminUnread);
        Assert.False(thread.TenantUnread);

        thread.MarkRead(byTenant: false);
        thread.Add(fromTenant: false, DateTimeOffset.UnixEpoch.AddHours(2));
        Assert.False(thread.AdminUnread);
        Assert.True(thread.TenantUnread);
    }
}
