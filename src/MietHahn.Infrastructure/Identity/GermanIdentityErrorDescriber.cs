using Microsoft.AspNetCore.Identity;

namespace MietHahn.Infrastructure.Identity;

/// <summary>Deutsche Fehlermeldungen für ASP.NET Core Identity.</summary>
internal sealed class GermanIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Errors(nameof(DefaultError), "Ein unbekannter Fehler ist aufgetreten.");

    public override IdentityError ConcurrencyFailure() => Errors(nameof(ConcurrencyFailure), "Der Datensatz wurde zwischenzeitlich geändert. Bitte erneut versuchen.");

    public override IdentityError PasswordMismatch() => Errors(nameof(PasswordMismatch), "Das Passwort ist falsch.");

    public override IdentityError InvalidToken() => Errors(nameof(InvalidToken), "Der Link ist ungültig oder abgelaufen.");

    public override IdentityError RecoveryCodeRedemptionFailed() => Errors(nameof(RecoveryCodeRedemptionFailed), "Der Wiederherstellungscode ist ungültig.");

    public override IdentityError LoginAlreadyAssociated() => Errors(nameof(LoginAlreadyAssociated), "Diese Anmeldung ist bereits einem Konto zugeordnet.");

    public override IdentityError InvalidUserName(string? userName) => Errors(nameof(InvalidUserName), $"Der Benutzername „{userName}“ ist ungültig.");

    public override IdentityError InvalidEmail(string? email) => Errors(nameof(InvalidEmail), $"Die E-Mail-Adresse „{email}“ ist ungültig.");

    public override IdentityError DuplicateUserName(string userName) => Errors(nameof(DuplicateUserName), $"Für „{userName}“ existiert bereits ein Konto.");

    public override IdentityError DuplicateEmail(string email) => Errors(nameof(DuplicateEmail), $"Für „{email}“ existiert bereits ein Konto.");

    public override IdentityError InvalidRoleName(string? role) => Errors(nameof(InvalidRoleName), $"Der Rollenname „{role}“ ist ungültig.");

    public override IdentityError DuplicateRoleName(string role) => Errors(nameof(DuplicateRoleName), $"Die Rolle „{role}“ existiert bereits.");

    public override IdentityError UserAlreadyHasPassword() => Errors(nameof(UserAlreadyHasPassword), "Für dieses Konto ist bereits ein Passwort festgelegt.");

    public override IdentityError UserLockoutNotEnabled() => Errors(nameof(UserLockoutNotEnabled), "Die Sperre ist für dieses Konto nicht aktiviert.");

    public override IdentityError UserAlreadyInRole(string role) => Errors(nameof(UserAlreadyInRole), $"Das Konto hat bereits die Rolle „{role}“.");

    public override IdentityError UserNotInRole(string role) => Errors(nameof(UserNotInRole), $"Das Konto hat die Rolle „{role}“ nicht.");

    public override IdentityError PasswordTooShort(int length) => Errors(nameof(PasswordTooShort), $"Das Passwort muss mindestens {length} Zeichen lang sein.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Errors(nameof(PasswordRequiresUniqueChars), $"Das Passwort muss mindestens {uniqueChars} verschiedene Zeichen enthalten.");

    public override IdentityError PasswordRequiresNonAlphanumeric() => Errors(nameof(PasswordRequiresNonAlphanumeric), "Das Passwort muss ein Sonderzeichen enthalten.");

    public override IdentityError PasswordRequiresDigit() => Errors(nameof(PasswordRequiresDigit), "Das Passwort muss eine Ziffer enthalten.");

    public override IdentityError PasswordRequiresLower() => Errors(nameof(PasswordRequiresLower), "Das Passwort muss einen Kleinbuchstaben enthalten.");

    public override IdentityError PasswordRequiresUpper() => Errors(nameof(PasswordRequiresUpper), "Das Passwort muss einen Großbuchstaben enthalten.");

    private static IdentityError Errors(string code, string description) => new() { Code = code, Description = description };
}
