namespace Elocaro.Domain.Heating;

/// <summary>
/// Monthly share of the annual degree days (Gradtagszahlen) for the apportionment of the heating consumption on a tenant change
/// without an interim reading (§ 9b para. 2 HeizkostenV).
/// </summary>
/// <param name="Version">Identifier of the table version, shown in the calculation protocol.</param>
/// <param name="ValidFromYear">First billing year (year of the end of the billing period) the version is used for.</param>
/// <param name="Status">Status of the data; versions that are not verified say so.</param>
/// <param name="Source">Where the values come from.</param>
/// <param name="PermillePerMonth">Share of each month (January to December) in permille of the year; sums to 1000.</param>
public sealed record DegreeDayTable(string Version, int ValidFromYear, string Status, string Source, IReadOnlyList<int> PermillePerMonth)
{
    /// <summary>Whether the values were checked against a published table.</summary>
    public bool Verified { get; init; }

    /// <summary>
    /// Measured degree days per month (key: first day of the month) of a weather station. If set, it covers every month the table is
    /// asked for and replaces <see cref="PermillePerMonth"/>; the weights then are degree days instead of permille (only their ratio counts).
    /// </summary>
    public IReadOnlyDictionary<DateOnly, decimal>? Measured { get; init; }

    /// <summary>Weight (permille of the year) of a single day: the share of its month spread evenly over the days of the month.</summary>
    public decimal DayWeight(DateOnly day) => Weight(day, day);

    /// <summary>
    /// Weight (permille of the year) of a period: per month the share of the month times the days in the period divided by the days of the
    /// month (multiplied first, so whole months are exact).
    /// </summary>
    public decimal Weight(DateOnly from, DateOnly to)
    {
        decimal sum = 0;
        var month = new DateOnly(from.Year, from.Month, 1);
        while (month <= to)
        {
            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            var first = month > from ? month : from;
            var monthEnd = new DateOnly(month.Year, month.Month, daysInMonth);
            var last = monthEnd < to ? monthEnd : to;
            var monthWeight = Measured is null ? PermillePerMonth[month.Month - 1] : Measured[month];
            sum += monthWeight * (last.DayNumber - first.DayNumber + 1) / daysInMonth;
            month = month.AddMonths(1);
        }

        return sum;
    }
}

/// <summary>
/// Versioned degree day tables. <b>Stand/prüfen:</b> the shipped table is a placeholder with a plausible seasonal course
/// (heating season October to April); it must be replaced with the table of the responsible source (e.g. VDI 3807 / Deutscher
/// Wetterdienst for the location) before a statement relies on it.
/// </summary>
public static class DegreeDayTables
{
    public static IReadOnlyList<DegreeDayTable> All { get; } =
    [
        new DegreeDayTable(
            "2026-stub",
            2000,
            "Stand/prüfen: Platzhalter, nicht aus einer amtlichen Tabelle geprüft",
            "Beispielhafter Jahresgang für Deutschland, nicht aus einer amtlichen Tabelle übernommen",
            [170, 145, 120, 75, 30, 10, 0, 0, 25, 75, 140, 210]),
    ];

    /// <summary>The newest version that is valid for the billing year.</summary>
    public static DegreeDayTable For(int billingYear) =>
        All.Where(t => t.ValidFromYear <= billingYear).OrderByDescending(t => t.ValidFromYear).FirstOrDefault() ?? All[0];
}
