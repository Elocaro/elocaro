namespace MietHahn.Application.Accounts;

/// <summary>Anlage und Einladung von Benutzerkonten sowie passwortlose Anmeldung (Magic Link).</summary>
public interface IAccountService
{
    /// <summary>Die Ersteinrichtung ist nur möglich, solange es noch keinen Benutzer gibt.</summary>
    Task<bool> InitialSetupPossibleAsync(CancellationToken cancellationToken = default);

    /// <summary>Legt den ersten Administrator an.</summary>
    Task<AccountResult> CreateFirstAdminAsync(string email, string displayName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt einen Benutzer ein. Rollen mit Passwort erhalten einen Link zum Festlegen des Passworts (7 Tage gültig),
    /// Mieter und Handwerker einen Anmeldelink (7 Tage gültig).
    /// </summary>
    Task<AccountResult> InviteAsync(string email, string displayName, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verschickt einen Anmeldelink (15 Minuten gültig), sofern ein passendes, berechtigtes Konto existiert.
    /// Gibt bewusst keine Auskunft darüber, ob die Adresse bekannt ist.
    /// </summary>
    Task RequestMagicLinkAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Löst einen Anmeldelink ein (einmalig) und liefert die Benutzer-Id oder <c>null</c>.</summary>
    Task<Guid?> RedeemMagicLinkAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Verschickt einen Link zum Zurücksetzen des Passworts (2 Stunden gültig); ohne Auskunft, ob die Adresse bekannt ist.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
}

public sealed record AccountResult(bool Succeeded, Guid? UserId, IReadOnlyList<string> Errors)
{
    public static AccountResult Ok(Guid id) => new(true, id, []);

    public static AccountResult Failed(params string[] errors) => new(false, null, errors);
}
