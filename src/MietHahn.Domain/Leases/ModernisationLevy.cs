using System.Globalization;

namespace MietHahn.Domain.Leases;

/// <summary>Outcome of the check of a rent increase after modernisation (§ 559 BGB).</summary>
/// <param name="Errors">The declaration would be invalid; it must not be sent.</param>
/// <param name="ChargeableCosts">Costs minus the share of maintenance.</param>
/// <param name="CalculatedIncrease">Monthly increase of 8 % of the chargeable costs a year, before the cap.</param>
/// <param name="CapMonthly">Highest increase within six years in total (per m² of living area).</param>
/// <param name="EarlierIncreases">Increases after modernisation within the last six years that count against the cap.</param>
/// <param name="AllowedIncrease">The monthly increase that may be demanded: the calculated one, limited by the cap.</param>
public sealed record ModernisationAssessment(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    decimal ChargeableCosts,
    decimal CalculatedIncrease,
    decimal CapPerSquareMetre,
    decimal CapMonthly,
    decimal EarlierIncreases,
    decimal AllowedIncrease)
{
    public bool IsAllowed => Errors.Count == 0;

    public bool IsCapped => AllowedIncrease < CalculatedIncrease;
}

/// <summary>Rules of the rent increase after modernisation (§§ 559 to 559c BGB, as amended 2019).</summary>
public static class ModernisationLevy
{
    /// <summary>8 % of the costs a year (§ 559 Abs. 1).</summary>
    public const decimal AnnualRate = 0.08m;

    /// <summary>Within six years the rent may rise by at most 3 € per m² per month (§ 559 Abs. 3a).</summary>
    public const int CapYears = 6;

    public const decimal CapPerSquareMetre = 3m;

    /// <summary>Where the rent before the increase is below 7 € per m², the limit is 2 € (§ 559 Abs. 3a Satz 2).</summary>
    public const decimal LowRentThreshold = 7m;

    public const decimal LowRentCapPerSquareMetre = 2m;

    /// <summary>Simplified procedure: costs up to 10,000 € per flat, 30 % deducted as maintenance (§ 559c).</summary>
    public const decimal SimplifiedCostLimit = 10000m;

    public const decimal SimplifiedMaintenanceShare = 0.30m;

    /// <summary>The increase applies from the start of the third month after receipt (§ 559b Abs. 2), six months later if the work was not announced or turned out over 10 % dearer.</summary>
    public static DateOnly EffectiveFrom(DateOnly received, bool extended)
    {
        var day = ComparativeRent.EffectiveFrom(received);
        return extended ? day.AddMonths(6) : day;
    }

    /// <summary>Monthly increase: costs times 8 % divided by twelve, commercially rounded.</summary>
    public static decimal MonthlyIncrease(decimal chargeableCosts) => Math.Round(chargeableCosts * AnnualRate / 12m, 2, MidpointRounding.AwayFromZero);

    public static decimal CapFor(decimal baseRentBefore, decimal area) =>
        area > 0 && baseRentBefore / area < LowRentThreshold ? LowRentCapPerSquareMetre : CapPerSquareMetre;

    public static ModernisationAssessment Assess(decimal baseRentBefore, decimal? area, decimal costs, decimal maintenanceShare, bool simplified, decimal earlierIncreases)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (simplified)
        {
            if (costs > SimplifiedCostLimit)
            {
                errors.Add($"Das vereinfachte Verfahren gilt nur bis {SimplifiedCostLimit:N0} € Kosten je Wohnung (§ 559c BGB).".Replace(",", ".", StringComparison.Ordinal));
            }

            maintenanceShare = Math.Round(costs * SimplifiedMaintenanceShare, 2, MidpointRounding.AwayFromZero);
            warnings.Add("Im vereinfachten Verfahren wird ein Pauschalanteil von 30 % für Instandhaltung abgezogen; es kann innerhalb von fünf Jahren nur einmal genutzt werden (§ 559c Abs. 4). Der Mieter kann sich nicht auf eine Härte berufen.");
        }

        if (costs <= 0)
        {
            errors.Add("Bitte die für die Wohnung aufgewendeten Kosten angeben.");
        }

        if (maintenanceShare < 0 || maintenanceShare >= costs && costs > 0)
        {
            errors.Add("Der Anteil für Instandhaltung (nicht umlagefähig, § 559 Abs. 2 BGB) muss kleiner sein als die Kosten.");
        }

        if (area is not > 0)
        {
            errors.Add("Für die Kappungsgrenze fehlt die Wohnfläche in den Stammdaten.");
        }

        var chargeable = Math.Max(0, costs - Math.Max(0, maintenanceShare));
        var calculated = MonthlyIncrease(chargeable);
        var capPerSquareMetre = area is > 0 ? CapFor(baseRentBefore, area.Value) : CapPerSquareMetre;
        var capMonthly = area is > 0 ? Math.Round(capPerSquareMetre * area.Value, 2, MidpointRounding.AwayFromZero) : 0m;
        var allowed = Math.Max(0, Math.Min(calculated, capMonthly - earlierIncreases));
        if (errors.Count == 0 && allowed <= 0)
        {
            errors.Add($"Die Kappungsgrenze ist ausgeschöpft: innerhalb von sechs Jahren sind höchstens {capPerSquareMetre.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))} € je m² möglich, davon wurden bereits {earlierIncreases.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))} € ausgeschöpft.");
        }
        else if (allowed < calculated)
        {
            warnings.Add($"Die Kappungsgrenze (§ 559 Abs. 3a BGB) begrenzt die Erhöhung auf {allowed.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))} € monatlich statt {calculated.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))} €.");
        }

        return new ModernisationAssessment(errors, warnings, chargeable, calculated, capPerSquareMetre, capMonthly, earlierIncreases, allowed);
    }
}
