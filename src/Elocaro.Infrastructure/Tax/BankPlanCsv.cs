using System.Globalization;
using Elocaro.Application.Tax;
using Elocaro.Infrastructure.Common;

namespace Elocaro.Infrastructure.Tax;

/// <summary>
/// Reads the repayment plan a bank hands out as CSV (columns found by their header: date, interest, optionally repayment and
/// remaining debt) and condenses it into interest and closing balance per calendar year.
/// </summary>
internal static class BankPlanCsv
{
    private static readonly string[] DateNames = ["datum", "fälligkeit", "fälligkeitsdatum", "zahlungstermin", "termin", "zahltermin", "raten-datum"];
    private static readonly string[] InterestNames = ["zinsen", "zinsanteil", "zins", "zinsen (eur)", "zinsen in eur"];
    private static readonly string[] BalanceNames = ["restschuld", "restkapital", "restdarlehen", "saldo", "restschuld (eur)", "restschuld nach zahlung"];
    private static readonly char[] Separators = [';', '\t', ','];
    private static readonly string[] Formats = ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "d.M.yyyy"];

    public static BankPlanPreview Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.TrimStart('﻿');
        var separator = Separator(text);
        var rows = CsvText.Rows(text, separator).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        var headerAt = rows.FindIndex(r => Find(r, DateNames) >= 0 && Find(r, InterestNames) >= 0);
        if (headerAt < 0)
        {
            return new BankPlanPreview([], [], ["In der Datei fehlen die Spalten „Datum“ (bzw. „Fälligkeit“) und „Zinsen“. Bitte den Tilgungsplan der Bank als CSV mit Kopfzeile verwenden."]);
        }

        var header = rows[headerAt];
        var dateColumn = Find(header, DateNames);
        var interestColumn = Find(header, InterestNames);
        var balanceColumn = Find(header, BalanceNames);
        var decimalSeparator = separator == ',' ? '.' : ',';

        var entries = new List<(DateOnly Date, decimal Interest, decimal? Balance)>();
        var errors = new List<string>();
        foreach (var (row, index) in rows.Skip(headerAt + 1).Select((r, i) => (r, i)))
        {
            var dateText = Cell(row, dateColumn);
            if (string.IsNullOrWhiteSpace(dateText))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(dateText.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                // Summary lines at the end ("Summe") are skipped, anything else that looks wrong is reported.
                if (!dateText.TrimStart().StartsWith("Summe", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Zeile {headerAt + index + 2}: Das Datum „{dateText.Trim()}“ ist nicht lesbar.");
                }

                continue;
            }

            var interest = CsvText.Amount(Cell(row, interestColumn), decimalSeparator);
            if (interest is null or < 0)
            {
                errors.Add($"Zeile {headerAt + index + 2}: Die Zinsen sind nicht lesbar.");
                continue;
            }

            entries.Add((date, interest.Value, balanceColumn >= 0 ? CsvText.Amount(Cell(row, balanceColumn), decimalSeparator) : null));
        }

        if (entries.Count == 0 && errors.Count == 0)
        {
            errors.Add("Die Datei enthält keine Raten.");
        }

        var years = entries.GroupBy(e => e.Date.Year).OrderBy(g => g.Key)
            .Select(g => new BankPlanYear(g.Key, g.Sum(e => e.Interest), g.OrderBy(e => e.Date).Last().Balance, g.Count())).ToList();

        // Years with fewer instalments than usual are only plausible at the start and end of the plan.
        var usual = years.Count == 0 ? 0 : years.GroupBy(j => j.Instalments).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var warnings = years.Where((j, i) => j.Instalments != usual && i > 0 && i < years.Count - 1)
            .Select(j => $"{j.Year}: {j.Instalments} statt {usual} Raten – bitte Sondertilgungen oder fehlende Zeilen prüfen.").ToList();
        if (years.Count > 0 && years[0].Instalments != usual)
        {
            warnings.Add($"{years[0].Year} enthält nur {years[0].Instalments} Raten; der Zins des Jahres ist nur vollständig, wenn der Plan ab Auszahlung geführt wird.");
        }

        return new BankPlanPreview(years, warnings, errors);
    }

    private static char Separator(string text)
    {
        var firstLines = string.Join('\n', text.Split('\n').Take(10));
        var counts = Separators.Select(c => (Char: c, Count: firstLines.Count(x => x == c))).OrderByDescending(x => x.Count).First();
        return counts.Count == 0 ? ';' : counts.Char;
    }

    private static int Find(List<string> header, string[] names) =>
        header.FindIndex(c => names.Contains(c.Trim().Trim('"').Trim().ToLowerInvariant()));

    private static string? Cell(List<string> row, int column) => column >= 0 && column < row.Count ? row[column] : null;
}
