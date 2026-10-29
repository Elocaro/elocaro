using Elocaro.Domain.Common;

namespace Elocaro.Domain.Statements;

public enum DispatchChannel
{
    /// <summary>Sent by email with the PDF attached; the outgoing mail is the proof of delivery.</summary>
    Email = 0,

    /// <summary>PDF downloaded for postal delivery; the date of dispatch is recorded manually.</summary>
    Post = 1,
}

/// <summary>
/// Delivery of a released statement to one tenancy: the letter (file and document), the booked claim or credit, the date of
/// dispatch and the proof of receipt. Unlike the statement itself this record is a log and gets completed later (receipt).
/// </summary>
public sealed class StatementDispatch : Entity
{
    private StatementDispatch()
    {
    }

    public StatementDispatch(
        Guid statementId,
        Guid leaseId,
        DispatchChannel channel,
        DateOnly sentOn,
        Guid fileId,
        Guid? documentId,
        Guid? mailId,
        decimal balance,
        decimal bookedAmount,
        Guid? chargeId,
        string? note)
    {
        if (channel == DispatchChannel.Email && mailId is null)
        {
            throw new RuleViolationException("Ein Versand per E-Mail braucht die ausgehende Mail als Nachweis.");
        }

        StatementId = statementId;
        LeaseId = leaseId;
        Channel = channel;
        SentOn = sentOn;
        FileId = fileId;
        DocumentId = documentId;
        MailId = mailId;
        Balance = balance;
        BookedAmount = bookedAmount;
        ChargeId = chargeId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid StatementId { get; private set; }

    public Guid LeaseId { get; private set; }

    public DispatchChannel Channel { get; private set; }

    public DateOnly SentOn { get; private set; }

    public Guid FileId { get; private set; }

    public Guid? DocumentId { get; private set; }

    /// <summary>Outgoing mail (proof of dispatch and delivery attempts).</summary>
    public Guid? MailId { get; private set; }

    /// <summary>Balance of the tenancy in the statement; positive = additional payment.</summary>
    public decimal Balance { get; private set; }

    /// <summary>Amount booked to the tenant account with this dispatch (for a correction only the difference to the earlier ones; 0 = nothing booked).</summary>
    public decimal BookedAmount { get; private set; }

    public Guid? ChargeId { get; private set; }

    /// <summary>E.g. why nothing was claimed.</summary>
    public string? Note { get; private set; }

    /// <summary>Day on which the statement reached the tenant (proof of receipt), if known.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    public string? ReceiptNote { get; private set; }

    public void ConfirmReceipt(DateOnly receivedOn, string? note)
    {
        if (receivedOn < SentOn)
        {
            throw new RuleViolationException("Der Zugang kann nicht vor dem Versand liegen.");
        }

        ReceivedOn = receivedOn;
        ReceiptNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
