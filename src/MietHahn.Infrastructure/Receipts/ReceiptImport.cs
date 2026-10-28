using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Receipts;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Receipts;

internal sealed class ReceiptImport(MietHahnDbContext db, IReceiptService receipts) : IReceiptImport
{
    private static readonly string[] DateNames = ["datum", "buchungsdatum", "belegdatum", "date"];
    private static readonly string[] GroupNames = ["gruppe", "kategorie", "kostenart", "konto", "rubrik", "category"];
    private static readonly string[] RecipientNames = ["empfänger", "zahlungsempfänger", "lieferant", "firma", "name", "empfaenger", "recipient"];
    private static readonly string[] AmountNames = ["betrag", "summe", "ausgabe", "ausgaben", "amount", "brutto"];
    private static readonly string[] NoteNames = ["info", "verwendungszweck", "bemerkung", "beschreibung", "text", "notiz", "note"];
    private static readonly char[] Separators = [';', '\t', ','];
    private static readonly string[] Formats = ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "d.M.yyyy"];

    /// <summary>Keywords of a group name and the cost type they point to (first hit wins).</summary>
    private static readonly (string Keyword, string CostType)[] Keywords =
    [
        ("abwasser", "betrkv-03"), ("entwässerung", "betrkv-03"), ("wasser", "betrkv-02"), ("grundsteuer", "betrkv-01"), ("öffentliche lasten", "betrkv-01"),
        ("müll", "betrkv-08"), ("abfall", "betrkv-08"), ("straßenreinigung", "betrkv-08"), ("schornstein", "betrkv-12"), ("kamin", "betrkv-12"),
        ("versicherung", "betrkv-13"), ("allgemeinstrom", "betrkv-11"), ("strom", "betrkv-11"), ("hausmeister", "betrkv-14"), ("hauswart", "betrkv-14"),
        ("garten", "betrkv-10"), ("aufzug", "betrkv-07"), ("gas", "betrkv-06"), ("heiz", "betrkv-06"), ("öl", "betrkv-06"), ("wartung", "betrkv-06"),
        ("reinigung", "betrkv-09"), ("zins", "schuldzinsen"), ("kredit", "schuldzinsen"), ("bank", "bankgebuehren"), ("kontoführung", "bankgebuehren"),
        ("steuerberat", "beratung"), ("anwalt", "beratung"), ("verwaltung", "verwaltung"), ("fahrt", "fahrtkosten"), ("modernisierung", "modernisierung"),
    ];

    public async Task<ReceiptImportPreview> PreviewAsync(Guid propertyId, byte[] csv, IReadOnlyDictionary<string, string?>? groupCostTypes = null, CancellationToken cancellationToken = default)
    {
        return await PlanAsync(csv, groupCostTypes, cancellationToken);
    }

    public async Task<ReceiptImportOutcome> ImportAsync(Guid propertyId, byte[] csv, IReadOnlyDictionary<string, string?>? groupCostTypes = null, CancellationToken cancellationToken = default)
    {
        var preview = await PlanAsync(csv, groupCostTypes, cancellationToken);
        if (!preview.CanImport)
        {
            return new ReceiptImportOutcome(false, preview.Errors.Count > 0 ? preview.Errors : ["Die Datei enthält keine neuen Zeilen mit Kostenart."], 0, preview.Rows.Count);
        }

        var imported = 0;
        var errors = new List<string>();
        foreach (var row in preview.Rows.Where(r => r.Status == ReceiptRowStatus.New))
        {
            var result = await receipts.CreateAsync(
                new ReceiptInput { SupplierName = row.Recipient, InvoiceDate = row.Date, Gross = row.Amount, Note = Note(row), PaidOn = row.Date },
                [],
                new CostItemInput { CostType = row.CostType!, PropertyId = propertyId },
                cancellationToken);
            if (result.Succeeded)
            {
                imported++;
            }
            else
            {
                errors.Add($"Zeile {row.Line}: {string.Join(" ", result.Errors)}");
            }
        }

        return new ReceiptImportOutcome(errors.Count == 0, errors, imported, preview.Rows.Count - imported);
    }

    private static string Note(ReceiptImportRow row) => $"Import Kassenbuch, Gruppe „{row.Group}“" + (string.IsNullOrWhiteSpace(row.Note) ? "" : $": {row.Note}");

    /// <summary>Cost type guessed from a group name, or <c>null</c>.</summary>
    internal static string? Suggest(string group)
    {
        var text = group.ToLowerInvariant();
        return Keywords.Where(k => text.Contains(k.Keyword, StringComparison.Ordinal)).Select(k => k.CostType).FirstOrDefault();
    }

    private async Task<ReceiptImportPreview> PlanAsync(byte[] csv, IReadOnlyDictionary<string, string?>? groupCostTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var text = TextDecoding.Read(csv).TrimStart('﻿');
        var first = string.Join('\n', text.Split('\n').Take(10));
        var separator = Separators.Select(c => (Char: c, Count: first.Count(x => x == c))).OrderByDescending(x => x.Count).First() is { Count: > 0 } best ? best.Char : ';';
        var decimalSeparator = separator == ',' ? '.' : ',';
        var rows = CsvText.Rows(text, separator).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        var headerAt = rows.FindIndex(r => Find(r, DateNames) >= 0 && Find(r, AmountNames) >= 0 && Find(r, GroupNames) >= 0);
        if (headerAt < 0)
        {
            return new ReceiptImportPreview([], [], ["In der Datei fehlen die Spalten „Datum“, „Gruppe“ und „Betrag“. Bitte den Export mit Kopfzeile verwenden (Excel: als CSV speichern)."]);
        }

        var header = rows[headerAt];
        var dateColumn = Find(header, DateNames);
        var groupColumn = Find(header, GroupNames);
        var recipientColumn = Find(header, RecipientNames);
        var amountColumn = Find(header, AmountNames);
        var noteColumn = Find(header, NoteNames);
        var existing = (await db.Set<Receipt>().AsNoTracking().Select(r => new { r.InvoiceDate, r.SupplierName, r.Gross }).ToListAsync(cancellationToken))
            .Select(r => ((DateOnly?)r.InvoiceDate, (r.SupplierName ?? "").Trim().ToLowerInvariant(), (decimal?)r.Gross)).ToHashSet();

        var parsed = new List<ReceiptImportRow>();
        var seen = new HashSet<(DateOnly?, string, decimal?)>();
        foreach (var (row, index) in rows.Skip(headerAt + 1).Select((r, i) => (r, i)))
        {
            var line = headerAt + index + 2;
            var dateText = Cell(row, dateColumn);
            var group = Cell(row, groupColumn);
            var amountText = Cell(row, amountColumn);
            if (dateText.Length == 0 && group.Length == 0 && amountText.Length == 0)
            {
                continue;
            }

            var datePart = dateText.Split(' ', 'T')[0];
            DateOnly? date = DateOnly.TryParseExact(datePart, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            var amount = CsvText.Amount(amountText, decimalSeparator);
            if (amount is { } value)
            {
                amount = Math.Abs(value);
            }

            var recipient = recipientColumn >= 0 ? Cell(row, recipientColumn) : "";
            var note = noteColumn >= 0 ? Cell(row, noteColumn) : null;
            var costType = ChosenCostType(group, groupCostTypes);
            string? problem = date is null ? $"Das Datum „{dateText}“ ist nicht lesbar."
                : amount is null or 0m ? "Der Betrag ist nicht lesbar oder 0."
                : null;
            var key = (date, recipient.Trim().ToLowerInvariant(), amount);
            var status = problem is not null ? ReceiptRowStatus.Invalid
                : costType is null ? ReceiptRowStatus.NoCostType
                : existing.Contains(key) || !seen.Add(key) ? ReceiptRowStatus.Duplicate
                : ReceiptRowStatus.New;
            parsed.Add(new ReceiptImportRow(line, date, group, recipient, amount, note, costType, status, problem));
        }

        var groups = parsed.GroupBy(r => r.Group).OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => new ReceiptImportGroup(g.Key, g.Count(), g.Sum(r => r.Amount ?? 0m), Suggest(g.Key), ChosenCostType(g.Key, groupCostTypes))).ToList();
        return new ReceiptImportPreview(parsed, groups, parsed.Count == 0 ? ["Die Datei enthält keine Zeilen."] : []);
    }

    private static string? ChosenCostType(string group, IReadOnlyDictionary<string, string?>? chosen) =>
        chosen is not null && chosen.TryGetValue(group, out var value) ? (string.IsNullOrWhiteSpace(value) ? null : value) : Suggest(group);

    private static int Find(List<string> header, string[] names) =>
        header.FindIndex(h => names.Contains(h.Trim().ToLowerInvariant()));

    private static string Cell(List<string> row, int index) => index >= 0 && index < row.Count ? row[index].Trim() : "";
}
