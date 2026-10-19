using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Aufgaben;
using MietHahn.Application.Bank;
using MietHahn.Application.Gemeinsam;
using MietHahn.Application.Konten;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Objekte;
using MietHahn.Infrastructure.Bank.Import;
using MietHahn.Infrastructure.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Bank;

internal sealed class Bankverwaltung(MietHahnDbContext db) : IBankverwaltung
{
    /// <summary>Kontoauszüge sind klein; größere Dateien sind fast sicher etwas anderes.</summary>
    internal const int MaxDateigroesse = 10 * 1024 * 1024;

    public async Task<IReadOnlyList<BankkontoZeile>> KontenAsync(CancellationToken cancellationToken = default)
    {
        var konten = await db.Set<Bankkonto>().AsNoTracking().OrderBy(k => k.Name).ToListAsync(cancellationToken);
        var objekte = await db.Set<Objekt>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Bezeichnung, cancellationToken);
        var statistik = await db.Set<Umsatz>().GroupBy(u => u.BankkontoId)
            .Select(g => new { Id = g.Key, Offen = g.Count(u => u.Status == Umsatzstatus.Offen), Letzter = g.Max(u => (DateOnly?)u.Buchungsdatum) })
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        return konten.Select(k => new BankkontoZeile(
            k.Id, k.Name, Iban.Formatieren(k.Iban), k.Bank, k.Typ,
            k.ObjektIds.Select(o => objekte.GetValueOrDefault(o, "?")).Order(StringComparer.CurrentCulture).ToList(),
            statistik.GetValueOrDefault(k.Id)?.Offen ?? 0,
            statistik.GetValueOrDefault(k.Id)?.Letzter)).ToList();
    }

    public async Task<BankkontoDaten?> KontoLadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var konto = await db.Set<Bankkonto>().AsNoTracking().SingleOrDefaultAsync(k => k.Id == id, cancellationToken);
        return konto is null ? null : new BankkontoDaten
        {
            Name = konto.Name,
            Iban = Iban.Formatieren(konto.Iban),
            Bic = konto.Bic,
            Bank = konto.Bank,
            Typ = konto.Typ,
            ObjektIds = konto.ObjektIds,
        };
    }

    public Task<Ergebnis> KontoAnlegenAsync(BankkontoDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var konto = new Bankkonto(daten.Name.Trim(), daten.Iban, daten.Typ);
            await UebernehmenAsync(konto, daten, cancellationToken);
            db.Add(konto);
            await SpeichernAsync(cancellationToken);
            return konto.Id;
        });

    public Task<Ergebnis> KontoAendernAsync(Guid id, BankkontoDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var konto = await db.Set<Bankkonto>().SingleOrDefaultAsync(k => k.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            var neueIban = Iban.Kompakt(daten.Iban);
            if (neueIban != konto.Iban && await db.Set<Umsatz>().AnyAsync(u => u.BankkontoId == id, cancellationToken))
            {
                throw new RegelverletzungException("Die IBAN eines Kontos mit Umsätzen lässt sich nicht ändern (Duplikaterkennung); bitte ein neues Konto anlegen.");
            }

            konto.IbanSetzen(daten.Iban);
            konto.Name = daten.Name.Trim();
            konto.Typ = daten.Typ;
            await UebernehmenAsync(konto, daten, cancellationToken);
            await SpeichernAsync(cancellationToken);
            return id;
        });

    public Task<Ergebnis> VorschauAnlegenAsync(Guid bankkontoId, string dateiname, byte[] inhalt, string? profil = null, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(inhalt);
            if (!await db.Set<Bankkonto>().AnyAsync(k => k.Id == bankkontoId, cancellationToken))
            {
                throw new NichtGefundenException();
            }

            if (inhalt.Length > MaxDateigroesse)
            {
                throw new RegelverletzungException("Die Datei ist größer als 10 MB.");
            }

            var gelesen = await LesenAsync(inhalt, string.IsNullOrWhiteSpace(profil) ? null : profil, cancellationToken);
            var import = new Bankimport(bankkontoId, Path.GetFileName(dateiname), inhalt, gelesen.Format, gelesen.Profil);
            db.Add(import);
            await db.SaveChangesAsync(cancellationToken);
            return import.Id;
        });

    public async Task<ImportVorschau?> VorschauAsync(Guid bankimportId, CancellationToken cancellationToken = default)
    {
        var import = await db.Set<Bankimport>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == bankimportId, cancellationToken);
        if (import is null)
        {
            return null;
        }

        var konto = await db.Set<Bankkonto>().AsNoTracking().SingleAsync(k => k.Id == import.BankkontoId, cancellationToken);
        if (import.Status == Importstatus.Importiert)
        {
            return new ImportVorschau(import.Id, konto.Id, konto.Name, import.Dateiname, import.Format, import.Profil, import.Status, null, [], []);
        }

        var gelesen = await LesenAsync(import.Inhalt, import.Profil, cancellationToken);
        var hashes = Umsatzfingerabdruck.Berechnen(konto.Iban, gelesen.Auszug.Umsaetze);
        var vorhanden = await VorhandeneAsync(konto.Id, hashes, cancellationToken);
        var abweichend = gelesen.Auszug.Iban is { } dateiIban && Iban.Kompakt(dateiIban) != konto.Iban ? Iban.Formatieren(dateiIban) : null;
        return new ImportVorschau(import.Id, konto.Id, konto.Name, import.Dateiname, import.Format, import.Profil, import.Status, abweichend,
            gelesen.Auszug.Umsaetze.Select((u, i) => new VorschauZeile(u, vorhanden.Contains(hashes[i]))).ToList(),
            gelesen.Auszug.Hinweise);
    }

    public Task<Ergebnis> ImportierenAsync(Guid bankimportId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var import = await db.Set<Bankimport>().SingleOrDefaultAsync(i => i.Id == bankimportId, cancellationToken) ?? throw new NichtGefundenException();
            if (import.Status == Importstatus.Importiert)
            {
                throw new RegelverletzungException("Diese Datei wurde bereits importiert.");
            }

            var konto = await db.Set<Bankkonto>().SingleAsync(k => k.Id == import.BankkontoId, cancellationToken);
            var gelesen = await LesenAsync(import.Inhalt, import.Profil, cancellationToken);
            if (gelesen.Auszug.Iban is { } dateiIban && Iban.Kompakt(dateiIban) != konto.Iban)
            {
                throw new RegelverletzungException($"Die Datei gehört zum Konto {Iban.Formatieren(dateiIban)}, nicht zu {konto.Name}.");
            }

            var umsaetze = gelesen.Auszug.Umsaetze;
            var hashes = Umsatzfingerabdruck.Berechnen(konto.Iban, umsaetze);
            var vorhanden = await VorhandeneAsync(konto.Id, hashes, cancellationToken);
            var neu = 0;
            for (var i = 0; i < umsaetze.Count; i++)
            {
                if (vorhanden.Add(hashes[i]))
                {
                    db.Add(new Umsatz(konto.Id, import.Id, umsaetze[i], hashes[i]));
                    neu++;
                }
            }

            import.Abschliessen(umsaetze.Count, neu, umsaetze.Count - neu,
                umsaetze.Count == 0 ? null : umsaetze.Min(u => u.Buchungsdatum),
                umsaetze.Count == 0 ? null : umsaetze.Max(u => u.Buchungsdatum));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (Datenbankfehler.IstEindeutigkeitsverletzung(ex))
            {
                throw new RegelverletzungException("Diese Umsätze werden gerade parallel importiert. Bitte die Seite neu laden.");
            }

            return import.Id;
        });

    public Task<Ergebnis> VerwerfenAsync(Guid bankimportId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var import = await db.Set<Bankimport>().SingleOrDefaultAsync(i => i.Id == bankimportId, cancellationToken) ?? throw new NichtGefundenException();
            if (import.Status == Importstatus.Importiert)
            {
                throw new RegelverletzungException("Ein abgeschlossener Import kann nicht verworfen werden.");
            }

            db.Remove(import);
            await db.SaveChangesAsync(cancellationToken);
            return import.BankkontoId;
        });

    public async Task<IReadOnlyList<ImportZeile>> ImporteAsync(Guid bankkontoId, CancellationToken cancellationToken = default) =>
        await db.Set<Bankimport>().AsNoTracking().Where(i => i.BankkontoId == bankkontoId).OrderByDescending(i => i.ErstelltAm)
            .Select(i => new ImportZeile(i.Id, i.ErstelltAm, i.ErstelltVon, i.Dateiname, i.Format, i.Profil, i.Status, i.Gelesen, i.Neu, i.Duplikate, i.Von, i.Bis))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UmsatzZeile>> UmsaetzeAsync(UmsatzSuche suche, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suche);
        var abfrage = db.Set<Umsatz>().AsNoTracking();
        if (suche.BankkontoId is { } konto)
        {
            abfrage = abfrage.Where(u => u.BankkontoId == konto);
        }

        if (suche.Status is { } status)
        {
            abfrage = abfrage.Where(u => u.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(suche.Text))
        {
            var muster = "%" + suche.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            abfrage = abfrage.Where(u => EF.Functions.ILike(u.Verwendungszweck, muster) || (u.GegenName != null && EF.Functions.ILike(u.GegenName, muster)) || (u.GegenIban != null && EF.Functions.ILike(u.GegenIban, muster)));
        }

        return await abfrage.OrderByDescending(u => u.Buchungsdatum).ThenByDescending(u => u.Id)
            .Select(u => new UmsatzZeile(u.Id, u.BankkontoId, u.Buchungsdatum, u.Valutadatum, u.Betrag, u.GegenName, u.GegenIban, u.Verwendungszweck, u.Buchungstext,
                u.EndToEndId, u.Mandatsreferenz, u.GlaeubigerId, u.Status, u.Notiz))
            .ToListAsync(cancellationToken);
    }

    public Task<Ergebnis> IgnorierenAsync(Guid umsatzId, string? notiz, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var umsatz = await db.Set<Umsatz>().SingleOrDefaultAsync(u => u.Id == umsatzId, cancellationToken) ?? throw new NichtGefundenException();
            umsatz.Ignorieren(string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim());
            await db.SaveChangesAsync(cancellationToken);
            return umsatz.Id;
        });

    public Task<Ergebnis> WiederOeffnenAsync(Guid umsatzId, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var umsatz = await db.Set<Umsatz>().SingleOrDefaultAsync(u => u.Id == umsatzId, cancellationToken) ?? throw new NichtGefundenException();
            umsatz.Oeffnen();
            await db.SaveChangesAsync(cancellationToken);
            return umsatz.Id;
        });

    public async Task<IReadOnlyList<CsvProfilDaten>> CsvProfileAsync(CancellationToken cancellationToken = default) =>
        (await ProfileAsync(cancellationToken)).Select(Daten).ToList();

    public Task<Ergebnis> CsvProfilSpeichernAsync(Guid? id, CsvProfilDaten daten, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(daten);
            var name = daten.Name.Trim();
            if (CsvVorgaben.Alle.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RegelverletzungException("Dieser Name gehört zu einem mitgelieferten Profil.");
            }

            var profil = id is { } vorhandenId
                ? await db.Set<CsvProfil>().SingleOrDefaultAsync(p => p.Id == vorhandenId, cancellationToken) ?? throw new NichtGefundenException()
                : db.Add(new CsvProfil(name, daten.SpalteBuchungsdatum.Trim())).Entity;
            profil.Name = name;
            profil.Trennzeichen = Zeichen(daten.Trennzeichen, ';');
            profil.Zeichensatz = daten.Zeichensatz is "utf-8" or "windows-1252" ? daten.Zeichensatz : "auto";
            profil.Datumsformat = daten.Datumsformat.Trim();
            profil.Dezimaltrennzeichen = Zeichen(daten.Dezimaltrennzeichen, ',');
            profil.SpalteBuchungsdatum = daten.SpalteBuchungsdatum.Trim();
            profil.SpalteValutadatum = Leer(daten.SpalteValutadatum);
            profil.SpalteBetrag = Leer(daten.SpalteBetrag);
            profil.SpalteSoll = Leer(daten.SpalteSoll);
            profil.SpalteHaben = Leer(daten.SpalteHaben);
            profil.SpalteName = Leer(daten.SpalteName);
            profil.SpalteIban = Leer(daten.SpalteIban);
            profil.SpalteBic = Leer(daten.SpalteBic);
            profil.SpalteVerwendungszweck = Leer(daten.SpalteVerwendungszweck);
            profil.SpalteBuchungstext = Leer(daten.SpalteBuchungstext);
            profil.SpalteEndToEnd = Leer(daten.SpalteEndToEnd);
            profil.SpalteMandatsreferenz = Leer(daten.SpalteMandatsreferenz);
            profil.SpalteGlaeubigerId = Leer(daten.SpalteGlaeubigerId);
            profil.Pruefen();
            if (profil.Datumsformat.Split('|').Any(f => f.Trim().Length == 0))
            {
                throw new RegelverletzungException("Bitte ein Datumsformat wie „dd.MM.yyyy“ angeben.");
            }

            await SpeichernAsync(cancellationToken);
            return profil.Id;
        });

    public Task<Ergebnis> CsvProfilLoeschenAsync(Guid id, CancellationToken cancellationToken = default) =>
        Befehl.AusfuehrenAsync(async () =>
        {
            var profil = await db.Set<CsvProfil>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NichtGefundenException();
            db.Remove(profil);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private async Task<GeleseneDatei> LesenAsync(byte[] inhalt, string? profil, CancellationToken cancellationToken)
    {
        try
        {
            return Bankdateileser.Lesen(inhalt, await ProfileAsync(cancellationToken), profil);
        }
        catch (BankdateiFehlerException ex)
        {
            throw new RegelverletzungException(ex.Message);
        }
        catch (FormatException ex)
        {
            throw new RegelverletzungException("Die Datei enthält ungültige Werte: " + ex.Message);
        }
    }

    private async Task<List<CsvProfil>> ProfileAsync(CancellationToken cancellationToken) =>
        [.. CsvVorgaben.Alle, .. await db.Set<CsvProfil>().AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken)];

    private async Task<HashSet<string>> VorhandeneAsync(Guid kontoId, IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        var liste = hashes.ToList();
        return (await db.Set<Umsatz>().IgnoreQueryFilters().Where(u => u.BankkontoId == kontoId && liste.Contains(u.Hash)).Select(u => u.Hash).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task UebernehmenAsync(Bankkonto konto, BankkontoDaten daten, CancellationToken cancellationToken)
    {
        konto.Bic = Leer(daten.Bic)?.ToUpperInvariant();
        konto.Bank = Leer(daten.Bank);
        // Ohne angehakte Objekte setzt die Formularbindung die Liste auf null.
        var objektIds = (daten.ObjektIds ?? []).Distinct().ToList();
        if (await db.Set<Objekt>().CountAsync(o => objektIds.Contains(o.Id), cancellationToken) != objektIds.Count)
        {
            throw new RegelverletzungException("Ein gewähltes Objekt wurde nicht gefunden.");
        }

        konto.ObjekteSetzen(objektIds);
        konto.Pruefen();
    }

    private async Task SpeichernAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Datenbankfehler.IstEindeutigkeitsverletzung(ex))
        {
            throw new RegelverletzungException("Ein Konto bzw. Profil mit dieser IBAN bzw. diesem Namen gibt es schon.");
        }
    }

    private static CsvProfilDaten Daten(CsvProfil p) => new()
    {
        Id = p.Vorgabe ? null : p.Id,
        Vorgabe = p.Vorgabe,
        Name = p.Name,
        Trennzeichen = p.Trennzeichen.ToString(),
        Zeichensatz = p.Zeichensatz,
        Datumsformat = p.Datumsformat,
        Dezimaltrennzeichen = p.Dezimaltrennzeichen.ToString(),
        SpalteBuchungsdatum = p.SpalteBuchungsdatum,
        SpalteValutadatum = p.SpalteValutadatum,
        SpalteBetrag = p.SpalteBetrag,
        SpalteSoll = p.SpalteSoll,
        SpalteHaben = p.SpalteHaben,
        SpalteName = p.SpalteName ?? p.SpalteNameEingang,
        SpalteIban = p.SpalteIban,
        SpalteBic = p.SpalteBic,
        SpalteVerwendungszweck = p.SpalteVerwendungszweck,
        SpalteBuchungstext = p.SpalteBuchungstext,
        SpalteEndToEnd = p.SpalteEndToEnd,
        SpalteMandatsreferenz = p.SpalteMandatsreferenz,
        SpalteGlaeubigerId = p.SpalteGlaeubigerId,
    };

    private static char Zeichen(string? wert, char standard) => wert switch
    {
        "\\t" or "Tab" => '\t',
        { Length: 1 } => wert[0],
        _ => standard,
    };

    private static string? Leer(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}

/// <summary>Dashboard: offene (nicht zugeordnete) Umsätze.</summary>
internal sealed class BankAufgaben(MietHahnDbContext db) : IAufgabenQuelle
{
    public async Task<IReadOnlyList<Aufgabe>> AufgabenAsync(ClaimsPrincipal benutzer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (!benutzer.IsInRole(Rollen.Admin) && !benutzer.HasClaim(Rechte.ClaimTyp, nameof(Recht.Finanzen)))
        {
            return [];
        }

        var offen = await db.Set<Umsatz>().CountAsync(u => u.Status == Umsatzstatus.Offen, cancellationToken);
        return offen == 0 ? [] : [new Aufgabe("Bank", $"{offen} Umsätze ohne Zuordnung", "/verwaltung/bank/umsaetze?status=Offen", Dringlichkeit.Bald,
            "Mieteingängen das Mietverhältnis zuordnen, private oder doppelte Umsätze ignorieren.")];
    }
}
