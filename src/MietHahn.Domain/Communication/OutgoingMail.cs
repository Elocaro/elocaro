using MietHahn.Domain.Common;

namespace MietHahn.Domain.Communication;

public enum MailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// An email in the outbox. It is stored fully rendered when enqueued (proof of what was sent)
/// and delivered with retries by a background job.
/// </summary>
[NoAudit]
public sealed class OutgoingMail : Entity
{
    /// <summary>After this many failed attempts it is no longer retried automatically.</summary>
    public const int MaxAttempts = 8;

    public const int MaxErrorLength = 2000;

    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(6);

    private OutgoingMail()
    {
        Subject = "";
        Html = "";
        Text = "";
    }

    public OutgoingMail(
        IEnumerable<string> to,
        string subject,
        string html,
        string text,
        DateTimeOffset now,
        IEnumerable<Guid>? attachmentFileIds = null,
        string? template = null,
        string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);

        To = to.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (To.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Empfänger ist erforderlich.", nameof(to));
        }

        if (To.FirstOrDefault(a => !a.Contains('@', StringComparison.Ordinal)) is { } invalid)
        {
            throw new ArgumentException($"Ungültige E-Mail-Adresse: {invalid}", nameof(to));
        }

        Subject = subject;
        Html = html;
        Text = text;
        AttachmentFileIds = attachmentFileIds?.ToList() ?? [];
        Template = template;
        Reference = reference;
        Status = MailStatus.Pending;
        NextAttemptAt = now;
    }

    public List<string> To { get; private set; } = [];

    public string Subject { get; private set; }

    public string Html { get; private set; }

    public string Text { get; private set; }

    public List<Guid> AttachmentFileIds { get; private set; } = [];

    /// <summary>Name of the template used (for traceability).</summary>
    public string? Template { get; private set; }

    /// <summary>Optional business reference, e.g. <c>Abrechnung:…</c>, as proof of delivery.</summary>
    public string? Reference { get; private set; }

    public MailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public void MarkSent(DateTimeOffset now)
    {
        Status = MailStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
    }

    public void MarkFailedAttempt(DateTimeOffset now, string errors)
    {
        Attempts++;
        LastError = errors is { Length: > MaxErrorLength } ? errors[..MaxErrorLength] : errors;

        if (Attempts >= MaxAttempts)
        {
            Status = MailStatus.Failed;
            return;
        }

        NextAttemptAt = now + RetryDelay(Attempts);
    }

    /// <summary>Manually resend a failed mail.</summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status == MailStatus.Sent)
        {
            throw new InvalidOperationException("Die Mail wurde bereits gesendet.");
        }

        Status = MailStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /// <summary>Exponential backoff: 1, 2, 4, 8 ... minutes, at most 6 hours.</summary>
    public static TimeSpan RetryDelay(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedAttempts);
        var minutes = Math.Pow(2, failedAttempts - 1);
        return minutes >= MaxDelay.TotalMinutes ? MaxDelay : TimeSpan.FromMinutes(minutes);
    }
}
