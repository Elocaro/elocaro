using Microsoft.AspNetCore.Identity;

namespace MietHahn.Infrastructure.Identitaet;

/// <summary>Deutsche Fehlermeldungen für ASP.NET Core Identity.</summary>
internal sealed class DeutscheIdentityFehler : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Fehler(nameof(DefaultError), "Ein unbekannter Fehler ist aufgetreten.");

    public override IdentityError ConcurrencyFailure() => Fehler(nameof(ConcurrencyFailure), "Der Datensatz wurde zwischenzeitlich geändert. Bitte erneut versuchen.");

    public override IdentityError PasswordMismatch() => Fehler(nameof(PasswordMismatch), "Das Passwort ist falsch.");

    public override IdentityError InvalidToken() => Fehler(nameof(InvalidToken), "Der Link ist ungültig oder abgelaufen.");

    public override IdentityError RecoveryCodeRedemptionFailed() => Fehler(nameof(RecoveryCodeRedemptionFailed), "Der Wiederherstellungscode ist ungültig.");

    public override IdentityError LoginAlreadyAssociated() => Fehler(nameof(LoginAlreadyAssociated), "Diese Anmeldung ist bereits einem Konto zugeordnet.");

    public override IdentityError InvalidUserName(string? userName) => Fehler(nameof(InvalidUserName), $"Der Benutzername „{userName}“ ist ungültig.");

    public override IdentityError InvalidEmail(string? email) => Fehler(nameof(InvalidEmail), $"Die E-Mail-Adresse „{email}“ ist ungültig.");

    public override IdentityError DuplicateUserName(string userName) => Fehler(nameof(DuplicateUserName), $"Für „{userName}“ existiert bereits ein Konto.");

    public override IdentityError DuplicateEmail(string email) => Fehler(nameof(DuplicateEmail), $"Für „{email}“ existiert bereits ein Konto.");

    public override IdentityError InvalidRoleName(string? role) => Fehler(nameof(InvalidRoleName), $"Der Rollenname „{role}“ ist ungültig.");

    public override IdentityError DuplicateRoleName(string role) => Fehler(nameof(DuplicateRoleName), $"Die Rolle „{role}“ existiert bereits.");

    public override IdentityError UserAlreadyHasPassword() => Fehler(nameof(UserAlreadyHasPassword), "Für dieses Konto ist bereits ein Passwort festgelegt.");

    public override IdentityError UserLockoutNotEnabled() => Fehler(nameof(UserLockoutNotEnabled), "Die Sperre ist für dieses Konto nicht aktiviert.");

    public override IdentityError UserAlreadyInRole(string role) => Fehler(nameof(UserAlreadyInRole), $"Das Konto hat bereits die Rolle „{role}“.");

    public override IdentityError UserNotInRole(string role) => Fehler(nameof(UserNotInRole), $"Das Konto hat die Rolle „{role}“ nicht.");

    public override IdentityError PasswordTooShort(int length) => Fehler(nameof(PasswordTooShort), $"Das Passwort muss mindestens {length} Zeichen lang sein.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Fehler(nameof(PasswordRequiresUniqueChars), $"Das Passwort muss mindestens {uniqueChars} verschiedene Zeichen enthalten.");

    public override IdentityError PasswordRequiresNonAlphanumeric() => Fehler(nameof(PasswordRequiresNonAlphanumeric), "Das Passwort muss ein Sonderzeichen enthalten.");

    public override IdentityError PasswordRequiresDigit() => Fehler(nameof(PasswordRequiresDigit), "Das Passwort muss eine Ziffer enthalten.");

    public override IdentityError PasswordRequiresLower() => Fehler(nameof(PasswordRequiresLower), "Das Passwort muss einen Kleinbuchstaben enthalten.");

    public override IdentityError PasswordRequiresUpper() => Fehler(nameof(PasswordRequiresUpper), "Das Passwort muss einen Großbuchstaben enthalten.");

    private static IdentityError Fehler(string code, string beschreibung) => new() { Code = code, Description = beschreibung };
}
