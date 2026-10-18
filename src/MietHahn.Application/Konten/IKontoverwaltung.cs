namespace MietHahn.Application.Konten;

/// <summary>Anlage und Einladung von Benutzerkonten sowie passwortlose Anmeldung (Magic Link).</summary>
public interface IKontoverwaltung
{
    /// <summary>Die Ersteinrichtung ist nur möglich, solange es noch keinen Benutzer gibt.</summary>
    Task<bool> ErsteinrichtungMoeglichAsync(CancellationToken cancellationToken = default);

    /// <summary>Legt den ersten Administrator an.</summary>
    Task<KontoErgebnis> ErstenAdminAnlegenAsync(string email, string anzeigename, string passwort, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt einen Benutzer ein. Rollen mit Passwort erhalten einen Link zum Festlegen des Passworts (7 Tage gültig),
    /// Mieter und Handwerker einen Anmeldelink (7 Tage gültig).
    /// </summary>
    Task<KontoErgebnis> EinladenAsync(string email, string anzeigename, string rolle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verschickt einen Anmeldelink (15 Minuten gültig), sofern ein passendes, berechtigtes Konto existiert.
    /// Gibt bewusst keine Auskunft darüber, ob die Adresse bekannt ist.
    /// </summary>
    Task AnmeldelinkAnfordernAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Löst einen Anmeldelink ein (einmalig) und liefert die Benutzer-Id oder <c>null</c>.</summary>
    Task<Guid?> AnmeldelinkEinloesenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Verschickt einen Link zum Zurücksetzen des Passworts (2 Stunden gültig); ohne Auskunft, ob die Adresse bekannt ist.</summary>
    Task PasswortZuruecksetzenAnfordernAsync(string email, CancellationToken cancellationToken = default);
}

public sealed record KontoErgebnis(bool Erfolgreich, Guid? BenutzerId, IReadOnlyList<string> Fehler)
{
    public static KontoErgebnis Ok(Guid id) => new(true, id, []);

    public static KontoErgebnis Fehlgeschlagen(params string[] fehler) => new(false, null, fehler);
}
