using System.Security.Cryptography;
using System.Text;
using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Identity;

/// <summary>
/// Einmal-Token für die passwortlose Anmeldung. In der Datenbank liegt nur der SHA-256-Hash; das Token selbst
/// steht ausschließlich im versendeten Link.
/// </summary>
[NoAudit]
[NoExport]
public sealed class MagicLink : Entity
{
    private MagicLink()
    {
        TokenHash = "";
    }

    private MagicLink(Guid userId, string tokenHash, DateTimeOffset validUntil)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ValidUntil = validUntil;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset ValidUntil { get; private set; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Erzeugt einen neuen Link und liefert das Klartext-Token (nur zum Versand).</summary>
    public static (MagicLink Link, string Token) Generate(Guid userId, DateTimeOffset now, TimeSpan validity)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new MagicLink(userId, Hash(token), now + validity), token);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool IsRedeemable(DateTimeOffset now) => RedeemedAt is null && now <= ValidUntil;

    public void Redeem(DateTimeOffset now)
    {
        if (!IsRedeemable(now))
        {
            throw new InvalidOperationException("Der Anmeldelink ist abgelaufen oder wurde bereits verwendet.");
        }

        RedeemedAt = now;
    }
}
