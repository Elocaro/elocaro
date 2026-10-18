using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using MietHahn.Application.Audit;
using MietHahn.Application.Betrieb;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Dateien;
using MietHahn.Infrastructure.Persistenz;

namespace MietHahn.Infrastructure.Betrieb;

/// <summary>
/// Vollexport aus dem EF-Modell: jede Tabelle als JSON und CSV, dazu alle Dateien und ein Manifest. Neue Entitäten
/// sind damit automatisch enthalten; Geheimnisse werden über <see cref="OhneExportAttribute"/> ausgenommen.
/// Gelesen wird in einer Repeatable-Read-Transaktion, der Export ist also ein konsistenter Stand.
/// </summary>
internal sealed class Datenexport(
    MietHahnDbContext db,
    IOptions<DateiablageOptionen> ablage,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr) : IDatenexport
{
    public const int FormatVersion = 1;

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");
    private static readonly UTF8Encoding Utf8MitBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonWriterOptions JsonOptionen = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Identity-Felder, die nur der Anmeldung dienen; Basisklassen lassen sich nicht mit Attributen versehen.
    private static readonly HashSet<string> GeheimeIdentityFelder =
    [
        nameof(IdentityUser<>.PasswordHash),
        nameof(IdentityUser<>.SecurityStamp),
        nameof(IdentityUser<>.ConcurrencyStamp),
    ];

    private static readonly Type[] GeheimeTabellen = [typeof(IdentityUserToken<Guid>), typeof(DataProtectionKey)];

    public string Dateiname() => $"miethahn-export-{Zeitzone.Heute(uhr.GetUtcNow()):yyyy-MM-dd}.zip";

    public async Task ExportierenAsync(Stream ziel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        var tabellen = ExportTabellen(db.Model);
        var erstelltAm = uhr.GetUtcNow();

        // Die Eintrags-Streams von ZipArchive schreiben beim Schließen synchron, was Kestrel verbietet. Daher erst in eine
        // temporäre Datei schreiben und dann asynchron kopieren; bei einem Fehler entsteht so auch kein halber Download.
        await using var puffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"miethahn-export-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var verbindung = db.Database.GetDbConnection();
            await using var transaktion = await verbindung.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await using (var zip = await ZipArchive.CreateAsync(puffer, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
            {
                var uebersicht = new List<(ExportTabelle Tabelle, long Zeilen)>();
                foreach (var tabelle in tabellen)
                {
                    var zeilen = await JsonSchreibenAsync(zip, transaktion, tabelle, cancellationToken);
                    await CsvSchreibenAsync(zip, transaktion, tabelle, cancellationToken);
                    uebersicht.Add((tabelle, zeilen));
                }

                var (dateien, fehlend) = await DateienSchreibenAsync(zip, transaktion, cancellationToken);
                await ManifestSchreibenAsync(zip, erstelltAm, uebersicht, dateien, fehlend, cancellationToken);
                await TextSchreibenAsync(zip, "LIESMICH.txt", Liesmich(erstelltAm), cancellationToken);
            }

            await transaktion.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        puffer.Position = 0;
        await puffer.CopyToAsync(ziel, cancellationToken);

        // Unabhängig von Audit:ZugriffeProtokollieren: ein Vollexport enthält alle personenbezogenen Daten.
        db.Add(new AuditEintrag(uhr.GetUtcNow(), benutzer.Kennung, "Datenexport", Guid.Empty, AuditAktion.Gelesen, "[]", "Vollexport aller Daten"));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Alle exportierten Tabellen mit ihren Spalten (ohne Geheimnisse und technische Spalten wie <c>xmin</c>).</summary>
    internal static IReadOnlyList<ExportTabelle> ExportTabellen(IModel model) =>
        model.GetRelationalModel().Tables
            .Where(t => !t.EntityTypeMappings.Any(m => IstGeheim(m.TypeBase.ClrType)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new ExportTabelle(
                t.Name,
                t.Columns
                    .Where(c => c.Name != "xmin" && !c.PropertyMappings.Any(m => IstGeheim(m.Property)))
                    .Select(c => new ExportSpalte(c.Name, c.StoreType))
                    .ToList(),
                t.PrimaryKey?.Columns.Select(c => c.Name).ToList() ?? []))
            .ToList();

    private static bool IstGeheim(Type typ) => typ.IsDefined(typeof(OhneExportAttribute), inherit: false) || GeheimeTabellen.Contains(typ);

    private static bool IstGeheim(IProperty eigenschaft) =>
        eigenschaft.PropertyInfo?.IsDefined(typeof(OhneExportAttribute)) == true
        || (typeof(IdentityUser<Guid>).IsAssignableFrom(eigenschaft.DeclaringType.ClrType) && GeheimeIdentityFelder.Contains(eigenschaft.Name));

    private static async Task<long> JsonSchreibenAsync(ZipArchive zip, DbTransaction transaktion, ExportTabelle tabelle, CancellationToken cancellationToken)
    {
        var eintrag = zip.CreateEntry($"daten/{tabelle.Name}.json", CompressionLevel.Optimal);
        await using var strom = await eintrag.OpenAsync(cancellationToken);
        await using var json = new Utf8JsonWriter(strom, JsonOptionen);
        await using var befehl = Abfrage(transaktion, tabelle);
        await using var leser = await befehl.ExecuteReaderAsync(cancellationToken);

        long zeilen = 0;
        json.WriteStartArray();
        while (await leser.ReadAsync(cancellationToken))
        {
            json.WriteStartObject();
            for (var i = 0; i < tabelle.Spalten.Count; i++)
            {
                json.WritePropertyName(tabelle.Spalten[i].Name);
                JsonWert(json, leser.GetValue(i), tabelle.Spalten[i].Typ);
            }

            json.WriteEndObject();
            if (++zeilen % 200 == 0)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
        return zeilen;
    }

    private static async Task CsvSchreibenAsync(ZipArchive zip, DbTransaction transaktion, ExportTabelle tabelle, CancellationToken cancellationToken)
    {
        var eintrag = zip.CreateEntry($"daten/{tabelle.Name}.csv", CompressionLevel.Optimal);
        await using var strom = await eintrag.OpenAsync(cancellationToken);
        await using var csv = new StreamWriter(strom, Utf8MitBom) { NewLine = "\r\n" };
        await csv.WriteLineAsync(string.Join(';', tabelle.Spalten.Select(s => CsvFeld(s.Name))));

        await using var befehl = Abfrage(transaktion, tabelle);
        await using var leser = await befehl.ExecuteReaderAsync(cancellationToken);
        var felder = new string[tabelle.Spalten.Count];
        while (await leser.ReadAsync(cancellationToken))
        {
            for (var i = 0; i < felder.Length; i++)
            {
                felder[i] = CsvFeld(CsvWert(leser.GetValue(i), tabelle.Spalten[i].Typ));
            }

            await csv.WriteLineAsync(string.Join(';', felder));
        }
    }

    private async Task<(long Anzahl, List<Guid> Fehlend)> DateienSchreibenAsync(ZipArchive zip, DbTransaction transaktion, CancellationToken cancellationToken)
    {
        var tabelle = db.Model.FindEntityType(typeof(Datei))!.GetTableName()!;
        await using var befehl = transaktion.Connection!.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = $"SELECT id, name, sha256 FROM {Bezeichner(tabelle)} ORDER BY id";
        await using var leser = await befehl.ExecuteReaderAsync(cancellationToken);

        long anzahl = 0;
        var fehlend = new List<Guid>();
        while (await leser.ReadAsync(cancellationToken))
        {
            var id = leser.GetGuid(0);
            var pfad = LokaleDateiablage.Speicherpfad(ablage.Value, leser.GetString(2));
            if (!File.Exists(pfad))
            {
                fehlend.Add(id);
                continue;
            }

            // Bilder und PDFs sind bereits komprimiert.
            var eintrag = zip.CreateEntry($"dateien/{id}/{SichererName(leser.GetString(1))}", CompressionLevel.Fastest);
            await using (var quelle = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var strom = await eintrag.OpenAsync(cancellationToken))
            {
                await quelle.CopyToAsync(strom, cancellationToken);
            }

            anzahl++;
        }

        return (anzahl, fehlend);
    }

    private async Task ManifestSchreibenAsync(
        ZipArchive zip, DateTimeOffset erstelltAm, List<(ExportTabelle Tabelle, long Zeilen)> tabellen, long dateien, List<Guid> fehlend, CancellationToken cancellationToken)
    {
        var eintrag = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var strom = await eintrag.OpenAsync(cancellationToken);
        await using var json = new Utf8JsonWriter(strom, JsonOptionen);
        json.WriteStartObject();
        json.WriteString("format", "MietHahn-Vollexport");
        json.WriteNumber("version", FormatVersion);
        json.WriteString("erstelltAm", erstelltAm.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("erstelltVon", benutzer.Kennung);
        json.WriteString("anwendung", typeof(Datenexport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        json.WriteStartArray("tabellen");
        foreach (var (tabelle, zeilen) in tabellen)
        {
            json.WriteStartObject();
            json.WriteString("name", tabelle.Name);
            json.WriteNumber("zeilen", zeilen);
            json.WriteStartArray("primaerschluessel");
            tabelle.Primaerschluessel.ToList().ForEach(json.WriteStringValue);
            json.WriteEndArray();
            json.WriteStartArray("spalten");
            foreach (var spalte in tabelle.Spalten)
            {
                json.WriteStartObject();
                json.WriteString("name", spalte.Name);
                json.WriteString("typ", spalte.Typ);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartObject("dateien");
        json.WriteNumber("anzahl", dateien);
        json.WriteStartArray("fehlend");
        fehlend.ForEach(json.WriteStringValue);
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
    }

    private static async Task TextSchreibenAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var eintrag = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var strom = await eintrag.OpenAsync(cancellationToken);
        await strom.WriteAsync(Utf8.GetBytes(text), cancellationToken);
    }

    private static DbCommand Abfrage(DbTransaction transaktion, ExportTabelle tabelle)
    {
        var befehl = transaktion.Connection!.CreateCommand();
        befehl.Transaction = transaktion;
        var reihenfolge = tabelle.Primaerschluessel.Count > 0 ? $" ORDER BY {string.Join(", ", tabelle.Primaerschluessel.Select(Bezeichner))}" : "";
#pragma warning disable CA2100 // Tabellen- und Spaltennamen stammen aus dem EF-Modell, nicht aus Eingaben.
        befehl.CommandText = $"SELECT {string.Join(", ", tabelle.Spalten.Select(s => Bezeichner(s.Name)))} FROM {Bezeichner(tabelle.Name)}{reihenfolge}";
#pragma warning restore CA2100
        return befehl;
    }

    private static string Bezeichner(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    internal static void JsonWert(Utf8JsonWriter json, object? wert, string typ)
    {
        switch (wert)
        {
            case null or DBNull:
                json.WriteNullValue();
                break;
            case string text when typ is "jsonb" or "json":
                json.WriteRawValue(text);
                break;
            case string text:
                json.WriteStringValue(text);
                break;
            case bool b:
                json.WriteBooleanValue(b);
                break;
            case Guid g:
                json.WriteStringValue(g);
                break;
            case short or int or long or decimal or double or float:
                json.WriteRawValue(Convert.ToString(wert, CultureInfo.InvariantCulture)!);
                break;
            case DateTime d when typ == "date":
                json.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case DateTime d:
                json.WriteStringValue(d.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateOnly d:
                json.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset d:
                json.WriteStringValue(d.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                json.WriteBase64StringValue(bytes);
                break;
            case Array liste:
                json.WriteStartArray();
                foreach (var element in liste)
                {
                    JsonWert(json, element, "");
                }

                json.WriteEndArray();
                break;
            default:
                json.WriteStringValue(Convert.ToString(wert, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>CSV für deutsches Excel: Dezimalkomma, Zeitpunkte in deutscher Zeit, Listen kommagetrennt.</summary>
    internal static string CsvWert(object? wert, string typ) => wert switch
    {
        null or DBNull => "",
        bool b => b ? "ja" : "nein",
        decimal or double or float => Convert.ToString(wert, Deutsch)!,
        DateTime d when typ == "date" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind)), Zeitzone.Deutschland)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset d => TimeZoneInfo.ConvertTime(d, Zeitzone.Deutschland).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        string text when typ is "jsonb" or "json" => text,
        // Formel-Injektion verhindern: Excel würde „=…“ ausführen (Daten können von Mietern stammen).
        string text when text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' => "'" + text,
        string text => text,
        Array liste => string.Join(", ", liste.Cast<object?>().Select(e => CsvWert(e, ""))),
        _ => Convert.ToString(wert, CultureInfo.InvariantCulture) ?? "",
    };

    internal static string CsvFeld(string wert) =>
        wert.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + wert.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : wert;

    private static string SichererName(string name)
    {
        var bereinigt = new string(name.Select(z => z is '/' or '\\' || char.IsControl(z) ? '_' : z).ToArray()).Trim();
        return bereinigt is "" or "." or ".." ? "datei" : bereinigt;
    }

    private static string Liesmich(DateTimeOffset erstelltAm) =>
        $"""
        MietHahn – Vollexport
        =====================

        Erstellt am {TimeZoneInfo.ConvertTime(erstelltAm, Zeitzone.Deutschland).ToString("dd.MM.yyyy HH:mm", Deutsch)} (deutsche Zeit).
        Der Export enthält alle Daten von MietHahn und ist ohne MietHahn lesbar.

        Inhalt
        ------
        manifest.json          Übersicht: Tabellen, Spalten mit Datentyp (PostgreSQL), Zeilenzahl, Dateien
        daten/<tabelle>.json   Alle Zeilen einer Tabelle als JSON-Array (UTF-8)
        daten/<tabelle>.csv    Dieselben Daten für Tabellenkalkulationen (Semikolon, UTF-8 mit BOM)
        dateien/<id>/<name>    Hochgeladene Dateien; <id> entspricht der Spalte „id“ in daten/dateien.json

        Formate
        -------
        JSON: Zeitpunkte in UTC nach ISO 8601 (z. B. 2026-10-18T09:30:00.0000000Z), Datumswerte als JJJJ-MM-TT,
              Beträge als Zahl mit Punkt.
        CSV:  Zeitpunkte in deutscher Zeit (JJJJ-MM-TT hh:mm:ss), Beträge mit Dezimalkomma, ja/nein für Wahrheitswerte.
              Texte, die mit = + - @ beginnen, tragen ein vorangestelltes ', damit Excel sie nicht als Formel ausführt.

        Verknüpfungen zwischen Tabellen laufen über Spalten „…_id“. Gelöschte Datensätze sind enthalten
        (Spalte „geloescht“ = true/ja), damit der Datenstand vollständig ist.

        Nicht enthalten
        ---------------
        Passwort-Hashes, Sicherheits- und Anmeldetokens, Anmeldelinks und die Schlüssel für Anmelde-Cookies.
        Für eine Wiederherstellung von MietHahn ist das Backup (docs/betrieb.md) gedacht, nicht dieser Export.
        """;
}

internal sealed record ExportTabelle(string Name, IReadOnlyList<ExportSpalte> Spalten, IReadOnlyList<string> Primaerschluessel);

internal sealed record ExportSpalte(string Name, string Typ);
