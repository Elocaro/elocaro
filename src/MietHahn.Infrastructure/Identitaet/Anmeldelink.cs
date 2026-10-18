using System.Security.Cryptography;
using System.Text;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Identitaet;

/// <summary>
/// Einmal-Token für die passwortlose Anmeldung. In der Datenbank liegt nur der SHA-256-Hash; das Token selbst
/// steht ausschließlich im versendeten Link.
/// </summary>
[OhneAudit]
[OhneExport]
public sealed class Anmeldelink : Entitaet
{
    private Anmeldelink()
    {
        TokenHash = "";
    }

    private Anmeldelink(Guid benutzerId, string tokenHash, DateTimeOffset gueltigBis)
    {
        BenutzerId = benutzerId;
        TokenHash = tokenHash;
        GueltigBis = gueltigBis;
    }

    public Guid BenutzerId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset GueltigBis { get; private set; }

    public DateTimeOffset? EingeloestAm { get; private set; }

    /// <summary>Erzeugt einen neuen Link und liefert das Klartext-Token (nur zum Versand).</summary>
    public static (Anmeldelink Link, string Token) Erzeugen(Guid benutzerId, DateTimeOffset jetzt, TimeSpan gueltigkeit)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new Anmeldelink(benutzerId, Hash(token), jetzt + gueltigkeit), token);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool IstEinloesbar(DateTimeOffset jetzt) => EingeloestAm is null && jetzt <= GueltigBis;

    public void Einloesen(DateTimeOffset jetzt)
    {
        if (!IstEinloesbar(jetzt))
        {
            throw new InvalidOperationException("Der Anmeldelink ist abgelaufen oder wurde bereits verwendet.");
        }

        EingeloestAm = jetzt;
    }
}
