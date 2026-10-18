using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Audit;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Identitaet;

/// <remarks>
/// Rollen- und Rechteänderungen wirken bei der nächsten Anfrage, weil der Security-Stamp-Validator den Principal
/// bei jeder Anfrage neu aus der Datenbank aufbaut (Intervall 0). Nur das Sperren erneuert den Stamp und meldet damit ab.
/// </remarks>
internal sealed class Benutzerverwaltung(MietHahnDbContext db, UserManager<Benutzer> benutzerManager, AuditSchreiber audit) : IBenutzerverwaltung
{
    public async Task<IReadOnlyList<BenutzerUebersicht>> ListeAsync(CancellationToken cancellationToken = default)
    {
        var benutzer = await db.Users.AsNoTracking().OrderBy(b => b.Anzeigename).ToListAsync(cancellationToken);
        var rollen = await (from ur in db.UserRoles
                            join r in db.Roles on ur.RoleId equals r.Id
                            select new { ur.UserId, r.Name }).ToListAsync(cancellationToken);
        var rechte = await db.UserClaims.Where(c => c.ClaimType == Rechte.ClaimTyp).ToListAsync(cancellationToken);

        return benutzer.Select(b => Uebersicht(
                b,
                rollen.Where(r => r.UserId == b.Id).Select(r => r.Name!).FirstOrDefault() ?? "",
                rechte.Where(c => c.UserId == b.Id).Select(c => Enum.Parse<Recht>(c.ClaimValue!)).ToList()))
            .ToList();
    }

    public async Task<BenutzerUebersicht?> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByIdAsync(id.ToString());
        if (benutzer is null)
        {
            return null;
        }

        var rolle = (await benutzerManager.GetRolesAsync(benutzer)).FirstOrDefault() ?? "";
        return Uebersicht(benutzer, rolle, await RechteVonAsync(benutzer));
    }

    public async Task<KontoErgebnis> RolleAendernAsync(Guid id, string rolle, CancellationToken cancellationToken = default)
    {
        if (!Rollen.Alle.Contains(rolle))
        {
            return KontoErgebnis.Fehlgeschlagen($"Unbekannte Rolle '{rolle}'.");
        }

        var benutzer = await benutzerManager.FindByIdAsync(id.ToString());
        if (benutzer is null)
        {
            return KontoErgebnis.Fehlgeschlagen("Konto nicht gefunden.");
        }

        var bisher = await benutzerManager.GetRolesAsync(benutzer);
        if (bisher.Contains(Rollen.Admin) && rolle != Rollen.Admin && await IstLetzterAktiverAdminAsync(benutzer, cancellationToken))
        {
            return KontoErgebnis.Fehlgeschlagen("Der letzte aktive Administrator kann seine Rolle nicht abgeben.");
        }

        await benutzerManager.RemoveFromRolesAsync(benutzer, bisher);
        await benutzerManager.AddToRoleAsync(benutzer, rolle);

        // Rechte gelten nur für Verwalter; beim Wechsel zum Verwalter gelten die Standardrechte.
        await RechteErsetzenAsync(benutzer, rolle == Rollen.Verwalter ? Rechte.VerwalterStandard : []);
        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Geaendert,
            [new Feldaenderung("Rolle", string.Join(", ", bisher), rolle)], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    public async Task<KontoErgebnis> RechteSetzenAsync(Guid id, IReadOnlyCollection<Recht> rechte, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByIdAsync(id.ToString());
        if (benutzer is null)
        {
            return KontoErgebnis.Fehlgeschlagen("Konto nicht gefunden.");
        }

        if (!await benutzerManager.IsInRoleAsync(benutzer, Rollen.Verwalter))
        {
            return KontoErgebnis.Fehlgeschlagen("Einzelrechte können nur für Verwalter festgelegt werden.");
        }

        var bisher = await RechteVonAsync(benutzer);
        await RechteErsetzenAsync(benutzer, rechte);
        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Geaendert,
            [new Feldaenderung("Rechte", string.Join(", ", bisher.Order()), string.Join(", ", rechte.Distinct().Order()))], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    public async Task<KontoErgebnis> SperrenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByIdAsync(id.ToString());
        if (benutzer is null)
        {
            return KontoErgebnis.Fehlgeschlagen("Konto nicht gefunden.");
        }

        if (await benutzerManager.IsInRoleAsync(benutzer, Rollen.Admin) && await IstLetzterAktiverAdminAsync(benutzer, cancellationToken))
        {
            return KontoErgebnis.Fehlgeschlagen("Der letzte aktive Administrator kann nicht gesperrt werden.");
        }

        benutzer.Gesperrt = true;
        await benutzerManager.UpdateAsync(benutzer);
        await benutzerManager.UpdateSecurityStampAsync(benutzer); // bestehende Sitzungen werden ungültig
        await db.Set<Anmeldelink>().Where(l => l.BenutzerId == id && l.EingeloestAm == null).ExecuteDeleteAsync(cancellationToken);
        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Geaendert, [new Feldaenderung("Gesperrt", "nein", "ja")], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    public async Task<KontoErgebnis> EntsperrenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByIdAsync(id.ToString());
        if (benutzer is null)
        {
            return KontoErgebnis.Fehlgeschlagen("Konto nicht gefunden.");
        }

        benutzer.Gesperrt = false;
        await benutzerManager.UpdateAsync(benutzer);
        await benutzerManager.SetLockoutEndDateAsync(benutzer, null);
        await benutzerManager.ResetAccessFailedCountAsync(benutzer);
        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Geaendert, [new Feldaenderung("Gesperrt", "ja", "nein")], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    private async Task<bool> IstLetzterAktiverAdminAsync(Benutzer benutzer, CancellationToken cancellationToken)
    {
        var admins = await benutzerManager.GetUsersInRoleAsync(Rollen.Admin);
        return !admins.Any(a => a.Id != benutzer.Id && !a.Gesperrt);
    }

    private async Task<List<Recht>> RechteVonAsync(Benutzer benutzer) =>
        (await benutzerManager.GetClaimsAsync(benutzer))
            .Where(c => c.Type == Rechte.ClaimTyp)
            .Select(c => Enum.Parse<Recht>(c.Value))
            .ToList();

    private async Task RechteErsetzenAsync(Benutzer benutzer, IReadOnlyCollection<Recht> rechte)
    {
        var bisher = (await benutzerManager.GetClaimsAsync(benutzer)).Where(c => c.Type == Rechte.ClaimTyp).ToList();
        if (bisher.Count > 0)
        {
            await benutzerManager.RemoveClaimsAsync(benutzer, bisher);
        }

        if (rechte.Count > 0)
        {
            await benutzerManager.AddClaimsAsync(benutzer, rechte.Distinct().Order().Select(r => new Claim(Rechte.ClaimTyp, r.ToString())));
        }
    }

    private static BenutzerUebersicht Uebersicht(Benutzer b, string rolle, IReadOnlyList<Recht> rechte) => new(
        b.Id,
        b.Anzeigename,
        b.Email ?? "",
        rolle,
        rolle == Rollen.Admin ? Rechte.Alle : rechte,
        b.TwoFactorEnabled,
        b.PasswordHash is not null,
        b.Gesperrt,
        b.EmailConfirmed,
        b.ErstelltAm,
        b.LetzteAnmeldungAm);
}
