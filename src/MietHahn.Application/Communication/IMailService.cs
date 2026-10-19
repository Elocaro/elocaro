using MietHahn.Domain.Communication;

namespace MietHahn.Application.Communication;

/// <summary>Insight into the outbox and manual retry (admin).</summary>
public interface IMailService
{
    Task<IReadOnlyList<MailOverview>> LatestAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Sets a failed or waiting mail to "retry immediately".</summary>
    Task RetryAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record MailOverview(
    Guid Id,
    IReadOnlyList<string> To,
    string Subject,
    MailStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset NextAttemptAt,
    string? LastError);
