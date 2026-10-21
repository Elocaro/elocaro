using MietHahn.Domain.Common;
using MietHahn.Domain.Tickets;

namespace MietHahn.Domain.Tests.Tickets;

public class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static Ticket NewTicket() =>
        new("Heizkörper kalt", "Im Bad wird der Heizkörper nicht warm.", TicketCategory.Heating, TicketUrgency.Normal, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Now);

    [Fact]
    public void A_new_ticket_starts_as_new_and_open()
    {
        var ticket = NewTicket();

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.True(ticket.IsOpen);
    }

    [Fact]
    public void A_description_is_required()
    {
        Assert.Throws<RuleViolationException>(() =>
            new Ticket("Titel", "  ", TicketCategory.Other, TicketUrgency.Normal, Guid.NewGuid(), null, null, null, Now));
    }

    [Fact]
    public void Closing_sets_the_time_and_a_closed_ticket_stays_closed()
    {
        var ticket = NewTicket();

        ticket.SetStatus(TicketStatus.Closed, Now);

        Assert.Equal(Now, ticket.ClosedAt);
        Assert.Throws<RuleViolationException>(() => ticket.SetStatus(TicketStatus.InProgress, Now));
        Assert.Throws<RuleViolationException>(() => ticket.Assign(Guid.NewGuid(), null));
    }

    [Fact]
    public void Assigning_sets_assignee_and_appointment()
    {
        var ticket = NewTicket();
        var person = Guid.NewGuid();

        ticket.Assign(person, new DateOnly(2026, 3, 10));

        Assert.Equal(person, ticket.AssigneeId);
        Assert.Equal(new DateOnly(2026, 3, 10), ticket.AppointmentOn);
    }

    [Fact]
    public void A_comment_needs_text()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TicketComment(Guid.NewGuid(), " ", false, null, "Anna"));
    }
}
