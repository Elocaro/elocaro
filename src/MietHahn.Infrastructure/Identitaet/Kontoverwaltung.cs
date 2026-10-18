using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Kommunikation;
using MietHahn.Application.Konten;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Identitaet;

internal sealed class Kontoverwaltung(
    MietHahnDbContext db,
    UserManager<Benutzer> benutzerManager,
    RoleManager<Rolle> rollenManager,
    IMailVersand mail,
    TimeProvider uhr,
    IOptions<AnwendungOptionen> anwendung,
    AuditSchreiber audit) : IKontoverwaltung
{
    public static readonly TimeSpan AnmeldelinkGueltigkeit = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan EinladungGueltigkeit = TimeSpan.FromDays(7);

    public async Task<bool> ErsteinrichtungMoeglichAsync(CancellationToken cancellationToken = default) =>
        !await db.Users.AnyAsync(cancellationToken);

    public async Task<KontoErgebnis> ErstenAdminAnlegenAsync(string email, string anzeigename, string passwort, CancellationToken cancellationToken = default)
    {
        await RollenSicherstellenAsync(rollenManager);
        if (!await ErsteinrichtungMoeglichAsync(cancellationToken))
        {
            return KontoErgebnis.Fehlgeschlagen("Die Ersteinrichtung ist bereits abgeschlossen.");
        }

        var benutzer = NeuerBenutzer(email, anzeigename);
        benutzer.EmailConfirmed = true;
        var ergebnis = await benutzerManager.CreateAsync(benutzer, passwort);
        if (!ergebnis.Succeeded)
        {
            return Fehler(ergebnis);
        }

        await benutzerManager.AddToRoleAsync(benutzer, Rollen.Admin);
        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Angelegt,
            [new("Email", null, benutzer.Email), new("Rolle", null, Rollen.Admin)], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    public async Task<KontoErgebnis> EinladenAsync(string email, string anzeigename, string rolle, CancellationToken cancellationToken = default)
    {
        if (!Rollen.Alle.Contains(rolle))
        {
            return KontoErgebnis.Fehlgeschlagen($"Unbekannte Rolle '{rolle}'.");
        }

        await RollenSicherstellenAsync(rollenManager);
        if (await benutzerManager.FindByEmailAsync(email) is not null)
        {
            return KontoErgebnis.Fehlgeschlagen("Für diese E-Mail-Adresse existiert bereits ein Konto.");
        }

        var benutzer = NeuerBenutzer(email, anzeigename);
        var ergebnis = await benutzerManager.CreateAsync(benutzer);
        if (!ergebnis.Succeeded)
        {
            return Fehler(ergebnis);
        }

        await benutzerManager.AddToRoleAsync(benutzer, rolle);
        if (rolle == Rollen.Verwalter)
        {
            await benutzerManager.AddClaimsAsync(benutzer, Rechte.VerwalterStandard.Select(r => new System.Security.Claims.Claim(Rechte.ClaimTyp, r.ToString())));
        }

        if (Rollen.BrauchtPasswort(rolle))
        {
            var token = await benutzerManager.GenerateUserTokenAsync(benutzer, TokenAnbieter.Einladung, TokenAnbieter.Einladung);
            var link = anwendung.Value.Link($"konto/einladung?benutzer={benutzer.Id}&code={Uri.EscapeDataString(token)}");
            await mail.EinreihenAsync(new MailAuftrag([email], "einladung-passwort",
                new { Anzeigename = benutzer.Anzeigename, Rolle = Rollen.Anzeigename(rolle), Link = link.ToString(), GueltigTage = EinladungGueltigkeit.Days }), cancellationToken);
        }
        else
        {
            var link = await AnmeldelinkErzeugenAsync(benutzer.Id, EinladungGueltigkeit, cancellationToken);
            await mail.EinreihenAsync(new MailAuftrag([email], "einladung-link",
                new { Anzeigename = benutzer.Anzeigename, Rolle = Rollen.Anzeigename(rolle), Link = link.ToString(), GueltigTage = EinladungGueltigkeit.Days }), cancellationToken);
        }

        await audit.ProtokollierenAsync(nameof(Benutzer), benutzer.Id, AuditAktion.Angelegt,
            [new("Email", null, benutzer.Email), new("Rolle", null, rolle)], cancellationToken);
        return KontoErgebnis.Ok(benutzer.Id);
    }

    public async Task AnmeldelinkAnfordernAsync(string email, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByEmailAsync(email);
        if (benutzer is null || benutzer.Gesperrt || !Rollen.DarfMagicLink(await benutzerManager.GetRolesAsync(benutzer)))
        {
            return;
        }

        var link = await AnmeldelinkErzeugenAsync(benutzer.Id, AnmeldelinkGueltigkeit, cancellationToken);
        await mail.EinreihenAsync(new MailAuftrag([benutzer.Email!], "anmeldelink",
            new { Anzeigename = benutzer.Anzeigename, Link = link.ToString(), GueltigMinuten = (int)AnmeldelinkGueltigkeit.TotalMinutes }), cancellationToken);
    }

    public async Task<Guid?> AnmeldelinkEinloesenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = Anmeldelink.Hash(token);
        var link = await db.Set<Anmeldelink>().SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        var jetzt = uhr.GetUtcNow();
        if (link is null || !link.IstEinloesbar(jetzt))
        {
            return null;
        }

        var benutzer = await benutzerManager.FindByIdAsync(link.BenutzerId.ToString());
        if (benutzer is null || benutzer.Gesperrt || !Rollen.DarfMagicLink(await benutzerManager.GetRolesAsync(benutzer)))
        {
            return null;
        }

        link.Einloesen(jetzt);
        if (!benutzer.EmailConfirmed)
        {
            benutzer.EmailConfirmed = true; // Wer den Link aus der Mail nutzt, hat die Adresse bestätigt.
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // gleichzeitig eingelöst
        }

        return benutzer.Id;
    }

    public async Task PasswortZuruecksetzenAnfordernAsync(string email, CancellationToken cancellationToken = default)
    {
        var benutzer = await benutzerManager.FindByEmailAsync(email);
        if (benutzer is null || benutzer.Gesperrt || !await benutzerManager.HasPasswordAsync(benutzer))
        {
            return;
        }

        var token = await benutzerManager.GeneratePasswordResetTokenAsync(benutzer);
        var link = anwendung.Value.Link($"konto/passwort-zuruecksetzen?benutzer={benutzer.Id}&code={Uri.EscapeDataString(token)}");
        await mail.EinreihenAsync(new MailAuftrag([benutzer.Email!], "passwort-zuruecksetzen",
            new { Anzeigename = benutzer.Anzeigename, Link = link.ToString() }), cancellationToken);
    }

    /// <summary>Legt fehlende Rollen an (idempotent).</summary>
    public static async Task RollenSicherstellenAsync(RoleManager<Rolle> rollenManager)
    {
        ArgumentNullException.ThrowIfNull(rollenManager);
        foreach (var rolle in Rollen.Alle)
        {
            if (!await rollenManager.RoleExistsAsync(rolle))
            {
                await rollenManager.CreateAsync(new Rolle(rolle));
            }
        }
    }

    private Benutzer NeuerBenutzer(string email, string anzeigename) => new()
    {
        UserName = email.Trim(),
        Email = email.Trim(),
        Anzeigename = anzeigename.Trim(),
        ErstelltAm = uhr.GetUtcNow(),
    };

    private async Task<Uri> AnmeldelinkErzeugenAsync(Guid benutzerId, TimeSpan gueltigkeit, CancellationToken cancellationToken)
    {
        var (link, token) = Anmeldelink.Erzeugen(benutzerId, uhr.GetUtcNow(), gueltigkeit);
        db.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return anwendung.Value.Link($"konto/link/{token}");
    }

    private static KontoErgebnis Fehler(IdentityResult ergebnis) =>
        KontoErgebnis.Fehlgeschlagen([.. ergebnis.Errors.Select(e => e.Description)]);
}
