using Elocaro.Domain.Common;

namespace Elocaro.Domain.Communication;

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

    /// <summary>Replaces the body once the content is gone; recipient, subject, time and status remain as proof.</summary>
    public const string PurgedContent = "[Inhalt entfernt]";

    /// <summary>Replaces the subject when a recipient is anonymized.</summary>
    public const string AnonymizedSubject = "[anonymisiert]";

    /// <summary>Replaces the address when a recipient is anonymized.</summary>
    public const string AnonymizedAddress = "anonymisiert@example.invalid";

    /// <summary>
    /// Templates whose body contains a link that works as a key (sign-in, password, signature). Their content lives only as long as
    /// it is needed for delivery. A new template with such a link belongs here.
    /// </summary>
    public static readonly IReadOnlyList<string> SecretTemplates =
        ["invitation-password", "invitation-link", "magic-link", "password-reset", "signature-request"];

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

    /// <remarks>
    /// Not exported: the export works per column and must never contain a link that still works, whatever the status of the mail.
    /// The proof of delivery (recipients, subject, status, times, template, reference) stays in the export.
    /// </remarks>
    [NoExport]
    public string Html { get; private set; }

    /// <inheritdoc cref="Html" path="/remarks"/>
    [NoExport]
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

    /// <summary>When the body was replaced with <see cref="PurgedContent"/>; such a mail can no longer be sent.</summary>
    public DateTimeOffset? ContentPurgedAt { get; private set; }

    public bool ContainsSecret => Template is not null && SecretTemplates.Contains(Template);

    public void MarkSent(DateTimeOffset now)
    {
        Status = MailStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
        PurgeSecretContent(now);
    }

    public void MarkFailedAttempt(DateTimeOffset now, string errors)
    {
        Attempts++;
        LastError = errors is { Length: > MaxErrorLength } ? errors[..MaxErrorLength] : errors;

        if (Attempts >= MaxAttempts)
        {
            Status = MailStatus.Failed;
            PurgeSecretContent(now);
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

        if (ContentPurgedAt is not null)
        {
            throw new RuleViolationException("Der Inhalt dieser Mail wurde entfernt, sie kann nicht erneut gesendet werden. Lösen Sie die Einladung oder den Link neu aus.");
        }

        Status = MailStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /// <summary>Replaces the body with a placeholder. A mail still waiting for delivery keeps its content.</summary>
    public void PurgeContent(DateTimeOffset now)
    {
        if (Status == MailStatus.Pending)
        {
            throw new InvalidOperationException("Eine wartende Mail braucht ihren Inhalt noch.");
        }

        Html = PurgedContent;
        Text = PurgedContent;
        ContentPurgedAt ??= now;
    }

    /// <summary>
    /// Removes a person from the mail: the address, subject, body and last error (it may quote the address) are replaced.
    /// A waiting mail is not delivered any more, not even to its other recipients, as its content is gone.
    /// </summary>
    public void AnonymizeRecipients(IReadOnlyCollection<string> addresses, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        To = To.Select(a => addresses.Contains(a, StringComparer.OrdinalIgnoreCase) ? AnonymizedAddress : a).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Subject = AnonymizedSubject;
        LastError = null;
        if (Status == MailStatus.Pending)
        {
            Status = MailStatus.Failed;
        }

        PurgeContent(now);
    }

    private void PurgeSecretContent(DateTimeOffset now)
    {
        if (ContainsSecret)
        {
            PurgeContent(now);
        }
    }

    /// <summary>Exponential backoff: 1, 2, 4, 8 ... minutes, at most 6 hours.</summary>
    public static TimeSpan RetryDelay(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedAttempts);
        var minutes = Math.Pow(2, failedAttempts - 1);
        return minutes >= MaxDelay.TotalMinutes ? MaxDelay : TimeSpan.FromMinutes(minutes);
    }
}
