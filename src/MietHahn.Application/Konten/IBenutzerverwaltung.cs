namespace MietHahn.Application.Konten;

/// <summary>Verwaltung bestehender Benutzerkonten (Admin bzw. Recht „Benutzerverwaltung“).</summary>
public interface IBenutzerverwaltung
{
    Task<IReadOnlyList<BenutzerUebersicht>> ListeAsync(CancellationToken cancellationToken = default);

    Task<BenutzerUebersicht?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ersetzt die Rolle des Kontos. Der letzte aktive Admin kann seine Rolle nicht verlieren.</summary>
    Task<KontoErgebnis> RolleAendernAsync(Guid id, string rolle, CancellationToken cancellationToken = default);

    /// <summary>Setzt die Einzelrechte eines Verwalters.</summary>
    Task<KontoErgebnis> RechteSetzenAsync(Guid id, IReadOnlyCollection<Recht> rechte, CancellationToken cancellationToken = default);

    /// <summary>Sperrt das Konto und beendet alle Sitzungen. Der letzte aktive Admin kann nicht gesperrt werden.</summary>
    Task<KontoErgebnis> SperrenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<KontoErgebnis> EntsperrenAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record BenutzerUebersicht(
    Guid Id,
    string Anzeigename,
    string Email,
    string Rolle,
    IReadOnlyList<Recht> Rechte,
    bool ZweiFaktorAktiv,
    bool HatPasswort,
    bool Gesperrt,
    bool EinladungAngenommen,
    DateTimeOffset ErstelltAm,
    DateTimeOffset? LetzteAnmeldungAm);
