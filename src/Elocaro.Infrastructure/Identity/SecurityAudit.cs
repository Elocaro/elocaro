using System.Text.Json;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Elocaro.Infrastructure.Identity;

internal sealed class SecurityAudit(ElocaroDbContext db, UserManager<ApplicationUser> userManager, TimeProvider clock) : ISecurityAudit
{
    private const int MaxAccountLength = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(SecurityEvent kind, string? email, Guid? userId, CancellationToken cancellationToken = default)
    {
        var account = Clean(email);
        if (userId is null && account is not null)
        {
            userId = (await userManager.FindByEmailAsync(account))?.Id;
        }

        var changes = new[] { new FieldChange("Ereignis", null, Text(kind)) };
        db.Add(new AuditEntry(clock.GetUtcNow(), account ?? "unbekannt", nameof(ApplicationUser), userId ?? Guid.Empty, AuditAction.SecurityEvent, JsonSerializer.Serialize(changes, Json)));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Strips control characters (log injection) and shortens the entered address.</summary>
    internal static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Trim().Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length > MaxAccountLength ? cleaned[..MaxAccountLength] : cleaned;
    }

    internal static string Text(SecurityEvent kind) => kind switch
    {
        SecurityEvent.SignedIn => "Angemeldet",
        SecurityEvent.SignInFailed => "Anmeldung fehlgeschlagen",
        SecurityEvent.TwoFactorFailed => "Zwei-Faktor-Code falsch",
        SecurityEvent.RecoveryCodeUsed => "Mit Wiederherstellungscode angemeldet",
        SecurityEvent.RecoveryCodeFailed => "Wiederherstellungscode falsch",
        SecurityEvent.LockedOut => "Konto nach Fehlversuchen gesperrt",
        SecurityEvent.PasswordReset => "Passwort zurückgesetzt",
        SecurityEvent.MagicLinkSignedIn => "Mit Anmeldelink angemeldet",
        _ => kind.ToString(),
    };
}
