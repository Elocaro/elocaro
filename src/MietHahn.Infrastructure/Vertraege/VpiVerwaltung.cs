using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Vertraege;
using MietHahn.Domain.Vertraege;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Vertraege;

internal sealed partial class VpiVerwaltung(MietHahnDbContext db) : IVpiVerwaltung
{
    private static readonly string[] Monatsnamen =
        ["januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "dezember"];

    public async Task<IReadOnlyList<VpiAnsicht>> ListeAsync(CancellationToken cancellationToken = default) =>
        await db.Set<VpiWert>().AsNoTracking().OrderByDescending(v => v.Monat)
            .Select(v => new VpiAnsicht(v.Id, v.Monat, v.Wert, v.Quelle))
            .ToListAsync(cancellationToken);

    public Task<Ergebnis> SetzenAsync(DateOnly monat, decimal wert, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var erster = new DateOnly(monat.Year, monat.Month, 1);
            var vorhanden = await db.Set<VpiWert>().SingleOrDefaultAsync(v => v.Monat == erster, cancellationToken);
            if (vorhanden is null)
            {
                vorhanden = new VpiWert(erster, wert);
                db.Add(vorhanden);
            }
            else
            {
                vorhanden.Aendern(wert, "manuell");
            }

            await db.SaveChangesAsync(cancellationToken);
            return vorhanden.Id;
        });

    public async Task<VpiImportErgebnis> ImportierenAsync(string csv, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var (werte, fehler) = Lesen(csv);
        var vorhanden = await db.Set<VpiWert>().ToDictionaryAsync(v => v.Monat, cancellationToken);
        int neu = 0, geaendert = 0, gleich = 0;
        foreach (var (monat, wert) in werte)
        {
            if (vorhanden.TryGetValue(monat, out var eintrag))
            {
                if (eintrag.Wert == decimal.Round(wert, 1))
                {
                    gleich++;
                    continue;
                }

                eintrag.Aendern(wert, "Import");
                geaendert++;
            }
            else
            {
                vorhanden[monat] = new VpiWert(monat, wert, "Import");
                db.Add(vorhanden[monat]);
                neu++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new VpiImportErgebnis(neu, geaendert, gleich, fehler);
    }

    /// <summary>Liest Monatswerte aus CSV-Zeilen; Kopf- und Fußzeilen ohne erkennbaren Monat werden übersprungen.</summary>
    internal static (List<(DateOnly Monat, decimal Wert)> Werte, List<string> Fehler) Lesen(string csv)
    {
        var werte = new Dictionary<DateOnly, decimal>();
        var fehler = new List<string>();
        foreach (var rohzeile in csv.Split('\n'))
        {
            var zeile = rohzeile.Trim().Trim('﻿');
            if (zeile.Length == 0)
            {
                continue;
            }

            var teile = zeile.Split([';', '\t'], StringSplitOptions.TrimEntries).Select(t => t.Trim('"')).Where(t => t.Length > 0).ToList();
            if (MonatErkennen(teile, out var monat, out var rest) && rest.Count > 0)
            {
                // GENESIS kennzeichnet fehlende Werte mit „...“, „.“, „-“ oder „x“.
                var roh = rest[^1];
                if (roh is "..." or "." or "-" or "x")
                {
                    continue;
                }

                if (decimal.TryParse(roh.Replace(".", ",", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out var wert) && wert is > 0 and < 1000)
                {
                    werte[monat] = wert;
                }
                else
                {
                    fehler.Add($"Wert nicht lesbar: „{zeile}“");
                }
            }
        }

        return (werte.OrderBy(w => w.Key).Select(w => (w.Key, w.Value)).ToList(), fehler);
    }

    private static bool MonatErkennen(List<string> teile, out DateOnly monat, out List<string> rest)
    {
        monat = default;
        rest = [];
        if (teile.Count == 0)
        {
            return false;
        }

        // „2026-01“ oder „01.2026“ in einer Spalte
        var einzeln = MonatImText().Match(teile[0]);
        if (einzeln.Success)
        {
            var jahr = int.Parse(einzeln.Groups["j1"].Success ? einzeln.Groups["j1"].Value : einzeln.Groups["j2"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(einzeln.Groups["m1"].Success ? einzeln.Groups["m1"].Value : einzeln.Groups["m2"].Value, CultureInfo.InvariantCulture);
            if (m is >= 1 and <= 12)
            {
                monat = new DateOnly(jahr, m, 1);
                rest = teile.Skip(1).ToList();
                return true;
            }
        }

        // „2026;Januar;…“ (GENESIS-Format)
        if (teile.Count >= 3 && teile[0].Length == 4 && int.TryParse(teile[0], NumberStyles.None, CultureInfo.InvariantCulture, out var j))
        {
            var index = Array.IndexOf(Monatsnamen, teile[1].ToLowerInvariant());
            if (index >= 0)
            {
                monat = new DateOnly(j, index + 1, 1);
                rest = teile.Skip(2).ToList();
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(?:(?<j1>\d{4})-(?<m1>\d{1,2})|(?<m2>\d{1,2})\.(?<j2>\d{4}))$")]
    private static partial Regex MonatImText();
}
