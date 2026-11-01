using System.Globalization;
using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Heating;

/// <summary>
/// Fetches the monthly degree days (VDI 3807) per weather station from the open data of the Deutscher Wetterdienst (GeoNutzV, source: DWD).
/// One file per month appears a few days after the end of the month. Months that are missing are fetched; the latest two are fetched again
/// because the DWD may correct them.
/// </summary>
internal sealed partial class DegreeDayImporter(ElocaroDbContext db, HttpClient http, TimeProvider clock, IOptions<DegreeDayOptions> options, ILogger<DegreeDayImporter> logger)
{
    /// <returns>Number of station months written.</returns>
    public async Task<int> ImportAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return 0;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var known = (await db.Set<DegreeDayValue>().AsNoTracking().Select(v => v.Month).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var written = 0;
        for (var month = thisMonth.AddYears(-options.Value.YearsBack); month < thisMonth; month = month.AddMonths(1))
        {
            if (known.Contains(month) && month < thisMonth.AddMonths(-2))
            {
                continue;
            }

            try
            {
                written += await ImportMonthAsync(month, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                Failed(logger, exception, month.ToString("MM/yyyy", CultureInfo.InvariantCulture));
                break;
            }
        }

        return written;
    }

    private async Task<int> ImportMonthAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var url = new Uri(options.Value.BaseUrl, $"gradtage_{month:yyyyMM}.csv");
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return 0;
        }

        response.EnsureSuccessStatusCode();
        var rows = DegreeDayCsv.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).Where(r => r.Month == month).ToList();
        if (rows.Count == 0)
        {
            return 0;
        }

        var existing = await db.Set<DegreeDayValue>().Where(v => v.Month == month).ToDictionaryAsync(v => v.StationId, cancellationToken);
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.StationId, out var value))
            {
                value.Update(row.StationName, row.DegreeDays, row.LongTermMean);
            }
            else
            {
                db.Add(new DegreeDayValue(row.StationId, row.StationName, month, row.DegreeDays, row.LongTermMean));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Degree days of {Month} could not be fetched from the DWD.")]
    private static partial void Failed(ILogger logger, Exception exception, string month);
}

/// <summary>Parser of the monthly files of the DWD (<c>gradtage_JJJJMM.csv</c>).</summary>
internal static class DegreeDayCsv
{
    internal sealed record Row(string StationId, string StationName, DateOnly Month, decimal DegreeDays, decimal? LongTermMean);

    /// <summary>
    /// Columns: ID; latitude; longitude; station; month (JJJJMM); days; degree days; heating days; mean 2009-2018. Comment lines start with
    /// <c>#</c>; −999.9 marks a missing value (rows without degree days are skipped).
    /// </summary>
    public static IEnumerable<Row> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var line in text.Split('\n'))
        {
            var fields = line.Trim().Split(';');
            if (line.TrimStart().StartsWith('#') || fields.Length < 7)
            {
                continue;
            }

            if (!DateOnly.TryParseExact(fields[4].Trim(), "yyyyMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) || Number(fields[6]) is not { } degreeDays)
            {
                continue;
            }

            yield return new Row(fields[0].Trim(), fields[3].Trim(), month, degreeDays, fields.Length > 8 ? Number(fields[8]) : null);
        }
    }

    private static decimal? Number(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > -900 ? value : null;
}

[DisallowConcurrentExecution]
internal sealed class DegreeDayJob(DegreeDayImporter importer) : IJob
{
    public static readonly JobKey Key = new("degree-days", "heating");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await importer.ImportAsync(cancellationToken);
    }
}
