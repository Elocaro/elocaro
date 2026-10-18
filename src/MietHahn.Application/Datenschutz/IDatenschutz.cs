using MietHahn.Application.Gemeinsam;

namespace MietHahn.Application.Datenschutz;

/// <summary>DSGVO: Auskunft (Art. 15) und Löschung/Anonymisierung nach Ablauf der Aufbewahrungsfrist (Art. 17).</summary>
public interface IDatenschutz
{
    /// <summary>Alle zur Person gespeicherten Daten als JSON (UTF-8). Der Abruf wird protokolliert.</summary>
    Task<byte[]?> AuskunftAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Personen, deren Daten nicht mehr benötigt werden (Vorschlag, nichts geschieht automatisch).</summary>
    Task<IReadOnlyList<Anonymisierungsvorschlag>> VorschlaegeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Entfernt Name, Kontaktdaten, Anschriften, IBANs und die Werte im Änderungsprotokoll endgültig.
    /// Nur ohne aktive Bezüge (z. B. laufende Verträge) möglich.
    /// </summary>
    Task<Ergebnis> AnonymisierenAsync(Guid personId, CancellationToken cancellationToken = default);
}

public sealed record Anonymisierungsvorschlag(Guid PersonId, string Anzeigename, string Grund, DateOnly? LetzterBezug);
