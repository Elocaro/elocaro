using MietHahn.Domain.Communication;

namespace MietHahn.Application.Communication;

/// <summary>Einsicht in die Outbox und manuelles Wiederholen (Admin).</summary>
public interface IMailService
{
    Task<IReadOnlyList<MailOverview>> LatestAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Setzt eine fehlgeschlagene oder wartende Mail auf „sofort erneut versuchen“.</summary>
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
