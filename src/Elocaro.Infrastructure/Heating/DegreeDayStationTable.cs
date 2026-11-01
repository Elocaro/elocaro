using Elocaro.Domain.Common;
using Elocaro.Domain.Heating;

namespace Elocaro.Infrastructure.Heating;

/// <summary>Builds the <see cref="DegreeDayTable"/> of a billing period from the stored degree days of a DWD weather station.</summary>
internal static class DegreeDayStationTable
{
    private const string Source = "Deutscher Wetterdienst, Monatsgradtage nach VDI 3807 (Innentemperatur 20 °C), Open Data, GeoNutzV";

    /// <summary>
    /// The measured degree days of the months of the period; if the DWD has not published all of them yet, the long-term mean of the
    /// calendar months (2009 to 2018) instead. <c>null</c> if the station has neither.
    /// </summary>
    public static DegreeDayTable? Build(IReadOnlyCollection<DegreeDayValue> values, Period period)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 || period.To is not { } end)
        {
            return null;
        }

        var name = values.OrderByDescending(v => v.Month).First().StationName;
        var id = values.First().StationId;
        var measured = values.ToDictionary(v => v.Month, v => v.DegreeDays);
        var months = new List<DateOnly>();
        for (var month = new DateOnly(period.From.Year, period.From.Month, 1); month <= end; month = month.AddMonths(1))
        {
            months.Add(month);
        }

        var means = new decimal?[12];
        foreach (var value in values.Where(v => v.LongTermMean is not null))
        {
            means[value.Month.Month - 1] = value.LongTermMean;
        }

        var permille = means.All(m => m is not null) && means.Sum(m => m!.Value) > 0 ? Permille(means.Select(m => m!.Value).ToList()) : null;

        if (months.All(measured.ContainsKey))
        {
            return new DegreeDayTable($"DWD {id}", period.From.Year, $"Gradtage der Station {name} für {months[0]:MM/yyyy} bis {months[^1]:MM/yyyy}", Source, (IReadOnlyList<int>?)permille ?? Flat)
            {
                Verified = true,
                Measured = months.Distinct().ToDictionary(m => m, m => measured[m]),
            };
        }

        return permille is null
            ? null
            : new DegreeDayTable($"DWD {id} (Mittel)", period.From.Year, $"Mittlerer Jahresgang 2009 bis 2018 der Station {name}; die Monatswerte des Zeitraums sind noch nicht veröffentlicht", Source, permille)
            {
                Verified = true,
            };
    }

    /// <summary>Share of each month in permille of the year; the rounding difference goes to the largest month so that the sum is 1000.</summary>
    private static List<int> Permille(List<decimal> values)
    {
        var total = values.Sum();
        var result = values.Select(v => (int)Math.Round(v * 1000m / total, MidpointRounding.AwayFromZero)).ToList();
        result[result.IndexOf(result.Max())] += 1000 - result.Sum();
        return result;
    }

    private static readonly int[] Flat = [.. Enumerable.Repeat(83, 12)];
}
