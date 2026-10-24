using MietHahn.Domain.Common;

namespace MietHahn.Domain.Metering;

public enum ReadingReportStatus
{
    Open = 0,
    Accepted = 1,
    Rejected = 2,
}

/// <summary>
/// A meter reading that a tenant reported in the portal. It is only a proposal: it becomes a reading of the meter when the landlord
/// accepts it (after the usual plausibility check), or it is rejected with a reason.
/// </summary>
public sealed class ReadingReport : Entity
{
    private ReadingReport()
    {
        ReportedBy = "";
    }

    public ReadingReport(Guid meterId, DateOnly date, decimal value, string reportedBy, string? note = null)
    {
        if (value < 0)
        {
            throw new RuleViolationException("Der Zählerstand darf nicht negativ sein.");
        }

        MeterId = meterId;
        Date = date;
        Value = decimal.Round(value, 3);
        ReportedBy = string.IsNullOrWhiteSpace(reportedBy) ? "Mieter" : reportedBy.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = ReadingReportStatus.Open;
    }

    public Guid MeterId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Value { get; private set; }

    /// <summary>Display name of the person who reported it.</summary>
    public string ReportedBy { get; private set; }

    public string? Note { get; private set; }

    public ReadingReportStatus Status { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionNote { get; private set; }

    public void Accept(DateTimeOffset now)
    {
        EnsureOpen();
        Status = ReadingReportStatus.Accepted;
        DecidedAt = now;
    }

    public void Reject(string? reason, DateTimeOffset now)
    {
        EnsureOpen();
        Status = ReadingReportStatus.Rejected;
        DecidedAt = now;
        DecisionNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureOpen()
    {
        if (Status != ReadingReportStatus.Open)
        {
            throw new RuleViolationException("Die Meldung wurde schon bearbeitet.");
        }
    }
}

/// <summary>Remembers that the yearly reminder to report meter readings went to a lease, so it goes out once per year.</summary>
[NoAudit]
public sealed class ReadingReminder : Entity
{
    private ReadingReminder()
    {
    }

    public ReadingReminder(Guid leaseId, int year, DateTimeOffset sentAt)
    {
        LeaseId = leaseId;
        Year = year;
        SentAt = sentAt;
    }

    public Guid LeaseId { get; private set; }

    public int Year { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}
