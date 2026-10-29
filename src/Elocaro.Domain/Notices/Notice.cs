using Elocaro.Domain.Common;

namespace Elocaro.Domain.Notices;

/// <summary>
/// A notice for all current tenants of a property, or only for those of some of its units (water shut-off, meter reading date, house rules).
/// It is shown in the portal from <see cref="ValidFrom"/> to <see cref="ValidUntil"/> (inclusive).
/// </summary>
public sealed class Notice : Entity
{
    public const int MaxAttachments = 5;

    private Notice()
    {
        Title = "";
        Body = "";
    }

    public Notice(string title, string body, Guid propertyId, DateOnly validFrom, DateOnly? validUntil, bool requiresAcknowledgement, IEnumerable<Guid>? unitIds = null)
    {
        UnitIds = unitIds is null ? [] : unitIds.Distinct().ToList();
        Title = title.Trim();
        Body = body.Trim();
        PropertyId = propertyId;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        RequiresAcknowledgement = requiresAcknowledgement;
        Validate();
    }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public Guid PropertyId { get; private set; }

    /// <summary>Units the notice is meant for; empty = the whole property.</summary>
    public List<Guid> UnitIds { get; private set; } = [];

    public bool AppliesToUnit(Guid unitId) => UnitIds.Count == 0 || UnitIds.Contains(unitId);

    public DateOnly ValidFrom { get; private set; }

    /// <summary><c>null</c> = until withdrawn.</summary>
    public DateOnly? ValidUntil { get; private set; }

    /// <summary>Tenants have to confirm that they have read it.</summary>
    public bool RequiresAcknowledgement { get; private set; }

    /// <summary>When the tenants were told by mail; <c>null</c> = not yet.</summary>
    public DateTimeOffset? TenantsNotifiedAt { get; private set; }

    public bool IsCurrent(DateOnly day) => ValidFrom <= day && (ValidUntil is null || ValidUntil >= day);

    public void MarkTenantsNotified(DateTimeOffset at) => TenantsNotifiedAt = at;

    /// <summary>Set when a notice the tenants were told about is withdrawn before its end; they get a mail about it once.</summary>
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public DateTimeOffset? WithdrawalNotifiedAt { get; private set; }

    public void MarkWithdrawalNotified(DateTimeOffset at) => WithdrawalNotifiedAt = at;

    /// <summary>Ends the validity (the notice stays in the archive).</summary>
    public void Withdraw(DateOnly day, DateTimeOffset at)
    {
        if (ValidUntil is null || ValidUntil > day)
        {
            ValidUntil = day < ValidFrom ? ValidFrom : day;
            if (TenantsNotifiedAt is not null)
            {
                WithdrawnAt = at;
            }
        }
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        if (string.IsNullOrWhiteSpace(Body))
        {
            throw new RuleViolationException("Bitte den Text des Aushangs angeben.");
        }

        if (ValidUntil is { } until && until < ValidFrom)
        {
            throw new RuleViolationException("Das Ende der Gültigkeit liegt vor dem Beginn.");
        }
    }
}

public sealed class NoticeFile : Entity
{
    private NoticeFile()
    {
    }

    public NoticeFile(Guid noticeId, Guid fileId)
    {
        NoticeId = noticeId;
        FileId = fileId;
    }

    public Guid NoticeId { get; private set; }

    public Guid FileId { get; private set; }
}

/// <summary>A tenant account has opened (or confirmed) a notice.</summary>
public sealed class NoticeRead : Entity
{
    private NoticeRead()
    {
    }

    public NoticeRead(Guid noticeId, Guid userId, DateTimeOffset readAt)
    {
        NoticeId = noticeId;
        UserId = userId;
        ReadAt = readAt;
    }

    public Guid NoticeId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReadAt { get; private set; }
}

/// <summary>A conversation between the tenants of a lease and the administration.</summary>
public sealed class MessageThread : Entity
{
    private MessageThread()
    {
        Subject = "";
    }

    public MessageThread(Guid leaseId, string subject, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        LeaseId = leaseId;
        Subject = subject.Trim();
        LastMessageAt = at;
    }

    public Guid LeaseId { get; private set; }

    public string Subject { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>The administration has written something the tenant has not opened yet.</summary>
    public bool TenantUnread { get; private set; }

    /// <summary>The tenant has written something the administration has not opened yet.</summary>
    public bool AdminUnread { get; private set; }

    public void Add(bool fromTenant, DateTimeOffset at)
    {
        LastMessageAt = at;
        AdminUnread = fromTenant;
        TenantUnread = !fromTenant;
    }

    public void MarkRead(bool byTenant)
    {
        if (byTenant)
        {
            TenantUnread = false;
        }
        else
        {
            AdminUnread = false;
        }
    }
}

public sealed class Message : Entity
{
    public const int MaxAttachments = 5;

    private Message()
    {
        Text = "";
        AuthorName = "";
    }

    public Message(Guid threadId, string text, bool fromTenant, Guid? authorUserId, string authorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ThreadId = threadId;
        Text = text.Trim();
        FromTenant = fromTenant;
        AuthorUserId = authorUserId;
        AuthorName = authorName;
    }

    public Guid ThreadId { get; private set; }

    public string Text { get; private set; }

    public bool FromTenant { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public string AuthorName { get; private set; }
}

public sealed class MessageFile : Entity
{
    private MessageFile()
    {
    }

    public MessageFile(Guid threadId, Guid messageId, Guid fileId)
    {
        ThreadId = threadId;
        MessageId = messageId;
        FileId = fileId;
    }

    public Guid ThreadId { get; private set; }

    public Guid MessageId { get; private set; }

    public Guid FileId { get; private set; }
}
