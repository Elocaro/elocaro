using MietHahn.Application.Common;

namespace MietHahn.Application.Privacy;

/// <summary>DSGVO: Auskunft (Art. 15) und Löschung/Anonymisierung nach Ablauf der Aufbewahrungsfrist (Art. 17).</summary>
public interface IPrivacyService
{
    /// <summary>Alle zur Person gespeicherten Daten als JSON (UTF-8). Der Abruf wird protokolliert.</summary>
    Task<byte[]?> AccessReportAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Personen, deren Daten nicht mehr benötigt werden (Vorschlag, nichts geschieht automatisch).</summary>
    Task<IReadOnlyList<AnonymizationSuggestion>> SuggestionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Entfernt Name, Kontaktdaten, Anschriften, IBANs und die Werte im Änderungsprotokoll endgültig.
    /// Nur ohne aktive Bezüge (z. B. laufende Verträge) möglich.
    /// </summary>
    Task<Result> AnonymizeAsync(Guid personId, CancellationToken cancellationToken = default);
}

public sealed record AnonymizationSuggestion(Guid PersonId, string DisplayName, string Reason, DateOnly? LastRelation);
