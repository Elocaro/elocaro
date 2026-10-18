using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Verwaltung;
using MietHahn.Domain.Verwaltung;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Verwaltung;

internal sealed class VermieterprofilVerwaltung(MietHahnDbContext db) : IVermieterprofilVerwaltung
{
    public async Task<VermieterprofilDaten?> LadenAsync(CancellationToken cancellationToken = default)
    {
        var profil = await ProfilAsync(cancellationToken);
        return profil is null ? null : new VermieterprofilDaten
        {
            Name = profil.Name,
            Zusatz = profil.Zusatz,
            Strasse = profil.Strasse,
            Plz = profil.Plz,
            Ort = profil.Ort,
            Telefon = profil.Telefon,
            Email = profil.Email,
            Iban = profil.Iban,
            Bic = profil.Bic,
            Bank = profil.Bank,
            Steuernummer = profil.Steuernummer,
        };
    }

    public async Task SpeichernAsync(VermieterprofilDaten daten, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(daten);
        var profil = await ProfilAsync(cancellationToken);
        if (profil is null)
        {
            profil = new Vermieterprofil(daten.Name.Trim(), daten.Strasse.Trim(), daten.Plz.Trim(), daten.Ort.Trim());
            db.Add(profil);
        }

        profil.Name = daten.Name.Trim();
        profil.Zusatz = Leer(daten.Zusatz);
        profil.Strasse = daten.Strasse.Trim();
        profil.Plz = daten.Plz.Trim();
        profil.Ort = daten.Ort.Trim();
        profil.Telefon = Leer(daten.Telefon);
        profil.Email = Leer(daten.Email);
        profil.Iban = IbanFormatieren(daten.Iban);
        profil.Bic = Leer(daten.Bic)?.ToUpperInvariant();
        profil.Bank = Leer(daten.Bank);
        profil.Steuernummer = Leer(daten.Steuernummer);
        profil.Pruefen();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>„de021203…“ → „DE02 1203 0000 …“ (Vierergruppen, wie auf Dokumenten üblich).</summary>
    internal static string? IbanFormatieren(string? iban) => Leer(iban) is { } wert ? Domain.Gemeinsam.Iban.Formatieren(wert) : null;

    private Task<Vermieterprofil?> ProfilAsync(CancellationToken cancellationToken) =>
        db.Set<Vermieterprofil>().OrderBy(v => v.ErstelltAm).FirstOrDefaultAsync(cancellationToken);

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
