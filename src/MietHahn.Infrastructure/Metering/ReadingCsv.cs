using System.Globalization;
using MietHahn.Infrastructure.Bank.Import;

namespace MietHahn.Infrastructure.Metering;

/// <summary>Reads the CSV export of a remote reading system into raw rows: meter number, date and reading.</summary>
internal static class ReadingCsv
{
    private static readonly string[] NumberNames = ["zählernummer", "zähler-nr", "zähler-nr.", "zählernr", "zählernr.", "geräte-nr", "geräte-nr.", "gerätenummer", "seriennummer", "serien-nr", "nummer", "nr", "device", "device id", "meter", "meter number", "meter id"];
    private static readonly string[] DateNames = ["datum", "ablesedatum", "stichtag", "ablesung am", "zeitpunkt", "date", "timestamp"];
    private static readonly string[] ValueNames = ["zählerstand", "stand", "wert", "ablesewert", "stichtagswert", "zählerstand (kwh)", "zählerstand (m³)", "value", "reading"];
    private static readonly char[] Separators = [';', '\t', ','];
    private static readonly string[] Formats = ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "d.M.yyyy"];

    public sealed record Row(int Line, string Number, DateOnly? Date, decimal? Value, string? Problem);

    /// <returns>The rows, or an error message if the columns are missing.</returns>
    public static (IReadOnlyList<Row> Rows, string? Error) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.TrimStart('﻿');
        var first = string.Join('\n', text.Split('\n').Take(10));
        var separator = Separators.Select(c => (Char: c, Count: first.Count(x => x == c))).OrderByDescending(x => x.Count).First() is { Count: > 0 } best ? best.Char : ';';
        var decimalSeparator = separator == ',' ? '.' : ',';
        var rows = CsvParser.Rows(text, separator).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        var headerAt = rows.FindIndex(r => Find(r, NumberNames) >= 0 && Find(r, DateNames) >= 0 && Find(r, ValueNames) >= 0);
        if (headerAt < 0)
        {
            return ([], "In der Datei fehlen die Spalten „Zählernummer“, „Datum“ und „Zählerstand“. Bitte den Export mit Kopfzeile verwenden.");
        }

        var header = rows[headerAt];
        var numberColumn = Find(header, NumberNames);
        var dateColumn = Find(header, DateNames);
        var valueColumn = Find(header, ValueNames);
        var result = new List<Row>();
        foreach (var (row, index) in rows.Skip(headerAt + 1).Select((r, i) => (r, i)))
        {
            var line = headerAt + index + 2;
            var number = Cell(row, numberColumn)?.Trim() ?? "";
            var dateText = Cell(row, dateColumn)?.Trim() ?? "";
            if (number.Length == 0 && dateText.Length == 0)
            {
                continue;
            }

            // Some systems write date and time into one field.
            var datePart = dateText.Split(' ', 'T')[0];
            DateOnly? date = DateOnly.TryParseExact(datePart, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
            var value = CsvParser.Amount(Cell(row, valueColumn), decimalSeparator);
            string? problem = number.Length == 0 ? "Die Zählernummer fehlt."
                : date is null ? $"Das Datum „{dateText}“ ist nicht lesbar."
                : value is null ? "Der Zählerstand ist nicht lesbar."
                : null;
            result.Add(new Row(line, number, date, value, problem));
        }

        return (result, null);
    }

    private static int Find(List<string> header, string[] names) =>
        header.FindIndex(c => names.Contains(c.Trim().Trim('"').Trim().ToLowerInvariant()));

    private static string? Cell(List<string> row, int column) => column >= 0 && column < row.Count ? row[column] : null;
}
