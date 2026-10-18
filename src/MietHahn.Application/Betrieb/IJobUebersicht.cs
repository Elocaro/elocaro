namespace MietHahn.Application.Betrieb;

/// <summary>Status der geplanten Hintergrundjobs für die Admin-Übersicht.</summary>
public interface IJobUebersicht
{
    Task<IReadOnlyList<JobStatus>> LadenAsync(CancellationToken cancellationToken = default);
}

public sealed record JobStatus(
    string Name,
    string? Beschreibung,
    DateTimeOffset? NaechsteAusfuehrung,
    DateTimeOffset? LetzterLaufAm,
    bool? LetzterLaufErfolgreich,
    string? LetzterFehler);
