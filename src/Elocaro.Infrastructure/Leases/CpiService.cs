using System.Globalization;
using System.Text.RegularExpressions;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Leases;

internal sealed partial class CpiService(ElocaroDbContext db) : ICpiService
{
    private static readonly string[] MonthNames =
        ["januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "dezember"];

    public async Task<IReadOnlyList<CpiView>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Set<CpiValue>().AsNoTracking().OrderByDescending(v => v.Month)
            .Select(v => new CpiView(v.Id, v.Month, v.Value, v.Source))
            .ToListAsync(cancellationToken);

    public Task<Result> SetAsync(DateOnly month, decimal value, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var first = new DateOnly(month.Year, month.Month, 1);
            var existing = await db.Set<CpiValue>().SingleOrDefaultAsync(v => v.Month == first, cancellationToken);
            if (existing is null)
            {
                existing = new CpiValue(first, value);
                db.Add(existing);
            }
            else
            {
                existing.Update(value, "manuell");
            }

            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        });

    public async Task<CpiImportResult> ImportAsync(string csv, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var (values, errors) = Read(csv);
        var existing = await db.Set<CpiValue>().ToDictionaryAsync(v => v.Month, cancellationToken);
        int newItem = 0, changed = 0, equal = 0;
        foreach (var (month, value) in values)
        {
            if (existing.TryGetValue(month, out var entry))
            {
                if (entry.Value == decimal.Round(value, 1))
                {
                    equal++;
                    continue;
                }

                entry.Update(value, "Import");
                changed++;
            }
            else
            {
                existing[month] = new CpiValue(month, value, "Import");
                db.Add(existing[month]);
                newItem++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new CpiImportResult(newItem, changed, equal, errors);
    }

    /// <summary>Reads monthly values from CSV lines; header and footer lines without a recognizable month are skipped.</summary>
    internal static (List<(DateOnly Month, decimal Value)> Values, List<string> Errors) Read(string csv)
    {
        var values = new Dictionary<DateOnly, decimal>();
        var errors = new List<string>();
        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim().Trim('﻿');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split([';', '\t'], StringSplitOptions.TrimEntries).Select(t => t.Trim('"')).Where(t => t.Length > 0).ToList();
            if (DetectMonth(parts, out var month, out var rest) && rest.Count > 0)
            {
                // GENESIS marks missing values with „...“, „.“, „-“ or „x“.
                var raw = rest[^1];
                if (raw is "..." or "." or "-" or "x")
                {
                    continue;
                }

                if (decimal.TryParse(raw.Replace(".", ",", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out var value) && value is > 0 and < 1000)
                {
                    values[month] = value;
                }
                else
                {
                    errors.Add($"Wert nicht lesbar: „{line}“");
                }
            }
        }

        return (values.OrderBy(w => w.Key).Select(w => (w.Key, w.Value)).ToList(), errors);
    }

    private static bool DetectMonth(List<string> parts, out DateOnly month, out List<string> rest)
    {
        month = default;
        rest = [];
        if (parts.Count == 0)
        {
            return false;
        }

        // „2026-01“ or „01.2026“ in a single column
        var single = MonthInText().Match(parts[0]);
        if (single.Success)
        {
            var year = int.Parse(single.Groups["j1"].Success ? single.Groups["j1"].Value : single.Groups["j2"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(single.Groups["m1"].Success ? single.Groups["m1"].Value : single.Groups["m2"].Value, CultureInfo.InvariantCulture);
            if (m is >= 1 and <= 12)
            {
                month = new DateOnly(year, m, 1);
                rest = parts.Skip(1).ToList();
                return true;
            }
        }

        // „2026;Januar;…“ (GENESIS format)
        if (parts.Count >= 3 && parts[0].Length == 4 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var j))
        {
            var index = Array.IndexOf(MonthNames, parts[1].ToLowerInvariant());
            if (index >= 0)
            {
                month = new DateOnly(j, index + 1, 1);
                rest = parts.Skip(2).ToList();
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(?:(?<j1>\d{4})-(?<m1>\d{1,2})|(?<m2>\d{1,2})\.(?<j2>\d{4}))$")]
    private static partial Regex MonthInText();
}
