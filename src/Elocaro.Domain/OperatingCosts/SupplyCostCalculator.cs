using System.Globalization;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.OperatingCosts;

/// <summary>Cost of one tariff segment of a billing period.</summary>
/// <param name="Period">Segment: tariff validity within the billing period.</param>
/// <param name="Quantity">Consumption in the price unit; <c>null</c> if it could not be determined.</param>
/// <param name="MeterQuantity">Consumption as counted by the meter (needed for fuel quantities).</param>
/// <param name="Months">Months billed with the base fee (fractions by days).</param>
public sealed record SupplyCostPart(Guid TariffId, Period Period, decimal? Quantity, decimal? MeterQuantity, decimal UnitPrice, decimal Months, decimal BaseFeePerMonth, decimal Amount, string Description);

public sealed record SupplyCostResult(IReadOnlyList<SupplyCostPart> Parts, IReadOnlyList<string> Problems, bool CoversWholePeriod);

/// <summary>Calculates the cost of a supply contract from consumption and tariffs, per tariff segment and rounded to the cent.</summary>
public static class SupplyCostCalculator
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <param name="consumption">Consumption of a period: price unit quantity, meter quantity, and the reason if it is missing.</param>
    public static SupplyCostResult Calculate(
        SupplyContractModel contract,
        Period billingPeriod,
        Func<Period, (decimal? Quantity, decimal? MeterQuantity, string? Missing)> consumption)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(consumption);
        var end = billingPeriod.To ?? throw new RuleViolationException("Der Abrechnungszeitraum braucht ein Ende.");
        var parts = new List<SupplyCostPart>();
        var problems = new List<string>();
        var covered = 0;
        foreach (var tariff in contract.Tariffs.OrderBy(t => t.Validity.From))
        {
            if (tariff.Validity.Intersect(billingPeriod) is not { } segment)
            {
                continue;
            }

            var segmentEnd = segment.To ?? end;
            segment = new Period(segment.From, segmentEnd);
            covered += segment.Days;
            var (quantity, meterQuantity, missing) = consumption(segment);
            if (quantity is null && tariff.UnitPrice > 0)
            {
                problems.Add($"{contract.Name}: Der Verbrauch {Range(segment)} fehlt. {missing}".Trim());
            }

            var months = Months(segment);
            var amount = decimal.Round((quantity ?? 0m) * tariff.UnitPrice + months * tariff.BaseFeePerMonth, 2, MidpointRounding.AwayFromZero);
            var unit = contract.PriceUnit == PriceUnit.CubicMeter ? "m³" : "kWh";
            var text = $"{contract.Name} nach Tarif {Range(segment)}: "
                + (tariff.UnitPrice > 0 ? $"{(quantity ?? 0m).ToString("0.###", German)} {unit} × {tariff.UnitPrice.ToString("0.######", German)} €" : "")
                + (tariff.UnitPrice > 0 && tariff.BaseFeePerMonth > 0 ? " + " : "")
                + (tariff.BaseFeePerMonth > 0 ? $"Grundpreis {months.ToString("0.##", German)} Monate × {tariff.BaseFeePerMonth.ToString("0.####", German)} €" : "");
            parts.Add(new SupplyCostPart(tariff.Id, segment, quantity, meterQuantity, tariff.UnitPrice, months, tariff.BaseFeePerMonth, amount, text));
        }

        return new SupplyCostResult(parts, problems, covered == billingPeriod.Days);
    }

    /// <summary>Months of a period: full calendar months count 1, partial months by days.</summary>
    public static decimal Months(Period period)
    {
        var to = period.To ?? throw new InvalidOperationException("Ein unbefristeter Zeitraum hat keine Monate.");
        decimal months = 0;
        for (var month = new DateOnly(period.From.Year, period.From.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var last = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            var from = month < period.From ? period.From : month;
            var until = last > to ? to : last;
            months += (until.DayNumber - from.DayNumber + 1m) / DateTime.DaysInMonth(month.Year, month.Month);
        }

        return decimal.Round(months, 6);
    }

    private static string Range(Period p) => $"{p.From:dd.MM.yyyy} bis {p.To:dd.MM.yyyy}";
}
