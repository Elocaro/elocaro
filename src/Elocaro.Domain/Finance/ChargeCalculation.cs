using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Finance;

public sealed record MonthlyItem(RentComponentType Component, decimal Amount, string Name, bool ProRata);

/// <summary>Monthly rents from the rent components, prorated by day for move-in, move-out or changes within the month.</summary>
public static class ChargeCalculation
{
    /// <param name="from">Earliest day for which rent is claimed (start of accounting).</param>
    public static IReadOnlyList<MonthlyItem> Month(Lease lease, int year, int month, DateOnly? from = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var calendarMonth = Period.Month(year, month);
        var bounds = calendarMonth.Intersect(lease.Term);
        if (from is { } start)
        {
            bounds = bounds?.Intersect(new Period(start));
        }

        if (bounds is not { } period)
        {
            return [];
        }

        var daysInMonth = calendarMonth.Days;
        var items = new List<MonthlyItem>();
        foreach (var group in lease.Components.GroupBy(b => b.Kind).OrderBy(g => g.Key))
        {
            var total = 0m;
            var days = 0;
            string? name = null;
            foreach (var component in group.OrderBy(b => b.Validity.From))
            {
                if (component.Validity.Intersect(period) is not { } part)
                {
                    continue;
                }

                // Sum unrounded; only the monthly amount is rounded to cents.
                total += component.Amount * part.Days / daysInMonth;
                days += part.Days;
                name = component.Name ?? name;
            }

            var amount = decimal.Round(total, 2, MidpointRounding.AwayFromZero);
            if (amount != 0)
            {
                var prorated = days < daysInMonth || group.Count(b => b.Validity.Overlaps(period)) > 1;
                items.Add(new MonthlyItem(group.Key, amount, Text(group.Key, name, year, month, prorated, days, daysInMonth), prorated));
            }
        }

        return items;
    }

    /// <summary>Months for which rent is due from <paramref name="from"/> up to and including <paramref name="toMonth"/>.</summary>
    public static IEnumerable<DateOnly> Months(Lease lease, DateOnly from, DateOnly toMonth)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var start = lease.Start > from ? lease.Start : from;
        var end = lease.End is { } e && e < toMonth ? e : toMonth;
        for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    public static string LabelText(RentComponentType kind) => kind switch
    {
        RentComponentType.BaseRent => "Kaltmiete",
        RentComponentType.OperatingCostPrepayment => "Betriebskostenvorauszahlung",
        RentComponentType.HeatingCostPrepayment => "Heizkostenvorauszahlung",
        RentComponentType.OperatingCostFlatRate => "Betriebskostenpauschale",
        RentComponentType.ParkingSpace => "Stellplatz",
        RentComponentType.FurnishingSurcharge => "Möblierungszuschlag",
        _ => "Sonstiges",
    };

    private static string Text(RentComponentType kind, string? name, int year, int month, bool prorated, int days, int daysInMonth)
    {
        var label = kind == RentComponentType.Miscellaneous && name is not null ? name : LabelText(kind);
        var text = $"{label} {month:00}/{year}";
        return prorated && days < daysInMonth ? $"{text} (anteilig {days}/{daysInMonth})" : text;
    }
}
