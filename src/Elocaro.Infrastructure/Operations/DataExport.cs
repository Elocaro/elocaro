using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Elocaro.Domain.Files;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Files;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Operations;

/// <summary>
/// Full export from the EF model: every table as JSON and CSV, plus all files and a manifest. New entities
/// are therefore included automatically; secrets are excluded via <see cref="NoExportAttribute"/>.
/// Reading happens in a repeatable-read transaction, so the export is a consistent snapshot.
/// </summary>
internal sealed class DataExport(
    ElocaroDbContext db,
    IOptions<FileStoreOptions> fileStore,
    ICurrentUser user,
    TimeProvider clock) : IDataExport
{
    public const int FormatVersion = 1;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonWriterOptions JsonOptions = new() { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Identity fields used only for sign-in; base classes cannot be annotated with attributes.
    private static readonly HashSet<string> SecretIdentityFields =
    [
        nameof(IdentityUser<>.PasswordHash),
        nameof(IdentityUser<>.SecurityStamp),
        nameof(IdentityUser<>.ConcurrencyStamp),
    ];

    private static readonly Type[] SecretTables = [typeof(IdentityUserToken<Guid>), typeof(DataProtectionKey)];

    public string FileName() => $"elocaro-export-{GermanTime.Today(clock.GetUtcNow()):yyyy-MM-dd}.zip";

    public async Task ExportAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var tables = ExportTables(db.Model);
        var createdAt = clock.GetUtcNow();

        // ZipArchive entry streams write synchronously on close, which Kestrel forbids. So write to a
        // temporary file first and then copy asynchronously; this also avoids a half-finished download on error.
        await using var buffer = new FileStream(
            Path.Combine(Path.GetTempPath(), $"elocaro-export-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await using (var zip = await ZipArchive.CreateAsync(buffer, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
            {
                var overview = new List<(ExportTable Table, long Rows)>();
                foreach (var table in tables)
                {
                    var lines = await WriteJsonAsync(zip, transaction, table, cancellationToken);
                    await WriteCsvAsync(zip, transaction, table, cancellationToken);
                    overview.Add((table, lines));
                }

                var (files, missing) = await WriteFilesAsync(zip, transaction, cancellationToken);
                await WriteManifestAsync(zip, createdAt, overview, files, missing, cancellationToken);
                await WriteTextAsync(zip, "LIESMICH.txt", Readme(createdAt), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken);

        // Independent of Audit:LogAccess: a full export contains all personal data.
        db.Add(new AuditEntry(clock.GetUtcNow(), user.Identifier, "Datenexport", Guid.Empty, AuditAction.Read, "[]", "Vollexport aller Daten"));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>All exported tables with their columns (without secrets and technical columns such as <c>xmin</c>).</summary>
    internal static IReadOnlyList<ExportTable> ExportTables(IModel model) =>
        model.GetRelationalModel().Tables
            .Where(t => !t.EntityTypeMappings.Any(m => IsSecret(m.TypeBase.ClrType)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new ExportTable(
                t.Name,
                t.Columns
                    .Where(c => c.Name != "xmin" && !c.PropertyMappings.Any(m => IsSecret(m.Property)))
                    .Select(c => new ExportColumn(c.Name, c.StoreType))
                    .ToList(),
                t.PrimaryKey?.Columns.Select(c => c.Name).ToList() ?? []))
            .ToList();

    private static bool IsSecret(Type type) => type.IsDefined(typeof(NoExportAttribute), inherit: false) || SecretTables.Contains(type);

    private static bool IsSecret(IProperty property) =>
        property.PropertyInfo?.IsDefined(typeof(NoExportAttribute)) == true
        || (typeof(IdentityUser<Guid>).IsAssignableFrom(property.DeclaringType.ClrType) && SecretIdentityFields.Contains(property.Name));

    private static async Task<long> WriteJsonAsync(ZipArchive zip, DbTransaction transaction, ExportTable table, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry($"data/{table.Name}.json", CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await using var json = new Utf8JsonWriter(stream, JsonOptions);
        await using var command = Query(transaction, table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        long lines = 0;
        json.WriteStartArray();
        while (await reader.ReadAsync(cancellationToken))
        {
            json.WriteStartObject();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                json.WritePropertyName(table.Columns[i].Name);
                JsonValue(json, reader.GetValue(i), table.Columns[i].Type);
            }

            json.WriteEndObject();
            if (++lines % 200 == 0)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
        return lines;
    }

    private static async Task WriteCsvAsync(ZipArchive zip, DbTransaction transaction, ExportTable table, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry($"data/{table.Name}.csv", CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await using var csv = new StreamWriter(stream, Utf8WithBom) { NewLine = "\r\n" };
        await csv.WriteLineAsync(string.Join(';', table.Columns.Select(s => CsvField(s.Name))));

        await using var command = Query(transaction, table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var fields = new string[table.Columns.Count];
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i] = CsvField(CsvValue(reader.GetValue(i), table.Columns[i].Type));
            }

            await csv.WriteLineAsync(string.Join(';', fields));
        }
    }

    private async Task<(long Count, List<Guid> Missing)> WriteFilesAsync(ZipArchive zip, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var table = db.Model.FindEntityType(typeof(StoredFile))!.GetTableName()!;
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT id, name, sha256 FROM {Identifier(table)} ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        long count = 0;
        var missing = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var path = LocalFileStore.StoragePath(fileStore.Value, reader.GetString(2));
            if (!File.Exists(path))
            {
                missing.Add(id);
                continue;
            }

            // Images and PDFs are already compressed.
            var entry = zip.CreateEntry($"files/{id}/{SafeName(reader.GetString(1))}", CompressionLevel.Fastest);
            await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var stream = await entry.OpenAsync(cancellationToken))
            {
                await source.CopyToAsync(stream, cancellationToken);
            }

            count++;
        }

        return (count, missing);
    }

    private async Task WriteManifestAsync(
        ZipArchive zip, DateTimeOffset createdAt, List<(ExportTable Table, long Rows)> tables, long files, List<Guid> missing, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await using var json = new Utf8JsonWriter(stream, JsonOptions);
        json.WriteStartObject();
        json.WriteString("format", "Elocaro-Vollexport");
        json.WriteNumber("version", FormatVersion);
        json.WriteString("createdAt", createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("createdBy", user.Identifier);
        json.WriteString("application", typeof(DataExport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        json.WriteStartArray("tables");
        foreach (var (table, lines) in tables)
        {
            json.WriteStartObject();
            json.WriteString("name", table.Name);
            json.WriteNumber("rows", lines);
            json.WriteStartArray("primaryKey");
            table.PrimaryKey.ToList().ForEach(json.WriteStringValue);
            json.WriteEndArray();
            json.WriteStartArray("columns");
            foreach (var column in table.Columns)
            {
                json.WriteStartObject();
                json.WriteString("name", column.Name);
                json.WriteString("type", column.Type);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartObject("stored_files");
        json.WriteNumber("count", files);
        json.WriteStartArray("missing");
        missing.ForEach(json.WriteStringValue);
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(Utf8.GetBytes(text), cancellationToken);
    }

    private static DbCommand Query(DbTransaction transaction, ExportTable table)
    {
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        var order = table.PrimaryKey.Count > 0 ? $" ORDER BY {string.Join(", ", table.PrimaryKey.Select(Identifier))}" : "";
#pragma warning disable CA2100 // Table and column names come from the EF model, not from user input.
        command.CommandText = $"SELECT {string.Join(", ", table.Columns.Select(s => Identifier(s.Name)))} FROM {Identifier(table.Name)}{order}";
#pragma warning restore CA2100
        return command;
    }

    private static string Identifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    internal static void JsonValue(Utf8JsonWriter json, object? value, string type)
    {
        switch (value)
        {
            case null or DBNull:
                json.WriteNullValue();
                break;
            case string text when type is "jsonb" or "json":
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
                json.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                break;
            case DateTime d when type == "date":
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
            case Array list:
                json.WriteStartArray();
                foreach (var element in list)
                {
                    JsonValue(json, element, "");
                }

                json.WriteEndArray();
                break;
            default:
                json.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>CSV for German Excel: decimal comma, timestamps in German time, lists comma-separated.</summary>
    internal static string CsvValue(object? value, string type) => value switch
    {
        null or DBNull => "",
        bool b => b ? "ja" : "nein",
        decimal or double or float => Convert.ToString(value, German)!,
        DateTime d when type == "date" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind)), GermanTime.Germany)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset d => TimeZoneInfo.ConvertTime(d, GermanTime.Germany).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        string text when type is "jsonb" or "json" => text,
        // Prevent formula injection: Excel would execute „=…“ (data may originate from tenants).
        string text when text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' => "'" + text,
        string text => text,
        Array list => string.Join(", ", list.Cast<object?>().Select(e => CsvValue(e, ""))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    internal static string CsvField(string value) =>
        value.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Select(z => z is '/' or '\\' || char.IsControl(z) ? '_' : z).ToArray()).Trim();
        return cleaned is "" or "." or ".." ? "datei" : cleaned;
    }

    private static string Readme(DateTimeOffset createdAt) =>
        $"""
        Elocaro – Vollexport
        =====================

        Erstellt am {TimeZoneInfo.ConvertTime(createdAt, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm", German)} (deutsche Zeit).
        Der Export enthält alle Daten von Elocaro und ist ohne Elocaro lesbar.

        Inhalt
        ------
        manifest.json          Übersicht: Tabellen, Spalten mit Datentyp (PostgreSQL), Zeilenzahl, Dateien
        data/<tabelle>.json    Alle Zeilen einer Tabelle als JSON-Array (UTF-8)
        data/<tabelle>.csv     Dieselben Daten für Tabellenkalkulationen (Semikolon, UTF-8 mit BOM)
        files/<id>/<name>      Hochgeladene Dateien; <id> entspricht der Spalte „id“ in data/stored_files.json

        Formate
        -------
        JSON: Zeitpunkte in UTC nach ISO 8601 (z. B. 2026-10-18T09:30:00.0000000Z), Datumswerte als JJJJ-MM-TT,
              Beträge als Zahl mit Punkt.
        CSV:  Zeitpunkte in deutscher Zeit (JJJJ-MM-TT hh:mm:ss), Beträge mit Dezimalkomma, ja/nein für Wahrheitswerte.
              Texte, die mit = + - @ beginnen, tragen ein vorangestelltes ', damit Excel sie nicht als Formel ausführt.

        Verknüpfungen zwischen Tabellen laufen über Spalten „…_id“. Gelöschte Datensätze sind enthalten
        (Spalte „deleted“ = true/ja), damit der Datenstand vollständig ist.

        Nicht enthalten
        ---------------
        Passwort-Hashes, Sicherheits- und Anmeldetokens, Anmeldelinks und die Schlüssel für Anmelde-Cookies.
        Für eine Wiederherstellung von Elocaro ist das Backup (docs/betrieb.md) gedacht, nicht dieser Export.
        """;
}

internal sealed record ExportTable(string Name, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<string> PrimaryKey);

internal sealed record ExportColumn(string Name, string Type);
