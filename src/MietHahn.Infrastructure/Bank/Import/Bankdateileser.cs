using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

internal sealed record GeleseneDatei(Bankformat Format, string? Profil, Kontoauszug Auszug);

/// <summary>Erkennt das Format (camt.053, MT940, CSV mit passendem Profil) und liest die Datei.</summary>
internal static class Bankdateileser
{
    public static GeleseneDatei Lesen(byte[] inhalt, IReadOnlyList<CsvProfil> profile, string? profilName = null)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        ArgumentNullException.ThrowIfNull(profile);
        if (inhalt.Length == 0)
        {
            throw new BankdateiFehlerException("Die Datei ist leer.");
        }

        if (profilName is null && Camt053Parser.Erkennt(inhalt))
        {
            return new GeleseneDatei(Bankformat.Camt053, null, Camt053Parser.Lesen(inhalt));
        }

        if (profilName is null && Mt940Parser.Erkennt(inhalt))
        {
            return new GeleseneDatei(Bankformat.Mt940, null, Mt940Parser.Lesen(inhalt));
        }

        var profil = profilName is not null
            ? profile.FirstOrDefault(p => p.Name == profilName) ?? throw new BankdateiFehlerException($"Unbekanntes CSV-Profil „{profilName}“.")
            : profile.Select(p => (Profil: p, Treffer: CsvParser.Treffer(p, inhalt))).Where(p => p.Treffer > 0).OrderByDescending(p => p.Treffer).Select(p => p.Profil).FirstOrDefault()
                ?? throw new BankdateiFehlerException("Das Dateiformat wurde nicht erkannt. Unterstützt: camt.053 (XML), MT940 und CSV (Profil wählen oder eigenes anlegen).");
        return new GeleseneDatei(Bankformat.Csv, profil.Name, CsvParser.Lesen(profil, inhalt));
    }
}
