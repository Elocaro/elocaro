using MietHahn.Domain.Bank;

namespace MietHahn.Infrastructure.Bank.Import;

internal sealed record ParsedFile(BankFormat Format, string? Profile, BankStatement MoveOut);

/// <summary>Erkennt das Format (camt.053, MT940, CSV mit passendem Profil) und liest die Datei.</summary>
internal static class BankFileReader
{
    public static ParsedFile Read(byte[] content, IReadOnlyList<CsvProfile> profiles, string? profileName = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profiles);
        if (content.Length == 0)
        {
            throw new BankFileErrorException("Die Datei ist leer.");
        }

        if (profileName is null && Camt053Parser.Detects(content))
        {
            return new ParsedFile(BankFormat.Camt053, null, Camt053Parser.Read(content));
        }

        if (profileName is null && Mt940Parser.Detects(content))
        {
            return new ParsedFile(BankFormat.Mt940, null, Mt940Parser.Read(content));
        }

        var profile = profileName is not null
            ? profiles.FirstOrDefault(p => p.Name == profileName) ?? throw new BankFileErrorException($"Unbekanntes CSV-Profil „{profileName}“.")
            : profiles.Select(p => (Profile: p, Hits: CsvParser.Hits(p, content))).Where(p => p.Hits > 0).OrderByDescending(p => p.Hits).Select(p => p.Profile).FirstOrDefault()
                ?? throw new BankFileErrorException("Das Dateiformat wurde nicht erkannt. Unterstützt: camt.053 (XML), MT940 und CSV (Profil wählen oder eigenes anlegen).");
        return new ParsedFile(BankFormat.Csv, profile.Name, CsvParser.Read(profile, content));
    }
}
