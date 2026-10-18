using MietHahn.Domain.Kommunikation;

namespace MietHahn.Application.Kommunikation;

/// <summary>Einsicht in die Outbox und manuelles Wiederholen (Admin).</summary>
public interface IMailVerwaltung
{
    Task<IReadOnlyList<MailUebersicht>> LetzteAsync(int anzahl, CancellationToken cancellationToken = default);

    /// <summary>Setzt eine fehlgeschlagene oder wartende Mail auf „sofort erneut versuchen“.</summary>
    Task ErneutVersuchenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record MailUebersicht(
    Guid Id,
    IReadOnlyList<string> An,
    string Betreff,
    MailStatus Status,
    int Versuche,
    DateTimeOffset ErstelltAm,
    DateTimeOffset? GesendetAm,
    DateTimeOffset NaechsterVersuchAm,
    string? LetzterFehler);
