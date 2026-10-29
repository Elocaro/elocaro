using System.Globalization;

namespace Elocaro.Domain.Leases;

/// <summary>Outcome of the check of a rent increase to the comparative rent (§ 558 BGB).</summary>
/// <param name="Errors">The demand would be invalid or ineffective; it must not be sent.</param>
/// <param name="Warnings">Notes that do not prevent the demand.</param>
/// <param name="EarliestDeclaration">Earliest receipt of the demand with which all waiting periods are met.</param>
/// <param name="EffectiveFrom">Day the increase takes effect if the demand is received on the checked day.</param>
/// <param name="ConsentDeadline">Last day of the consent period.</param>
/// <param name="CapLimit">Highest rent allowed by the cap limit.</param>
/// <param name="MaxRent">Highest permissible rent: the lower one of cap limit and comparative rent.</param>
public sealed record ComparativeRentAssessment(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateOnly EarliestDeclaration,
    DateOnly EffectiveFrom,
    DateOnly ConsentDeadline,
    decimal CapLimit,
    decimal MaxRent)
{
    public bool IsAllowed => Errors.Count == 0;
}

/// <summary>Rules of the rent increase to the local comparative rent (§§ 558 to 558b BGB): waiting periods, cap limit, dates.</summary>
public static class ComparativeRent
{
    /// <summary>The rent must have been unchanged for 15 months when the increase takes effect (§ 558 Abs. 1 Satz 1).</summary>
    public const int WaitMonths = 15;

    /// <summary>The demand can be made one year after the last increase at the earliest (§ 558 Abs. 1 Satz 2).</summary>
    public const int LockoutMonths = 12;

    /// <summary>The cap limit looks back three years (§ 558 Abs. 3).</summary>
    public const int CapYears = 3;

    public const int StandardCapPercent = 20;

    public const int TightMarketCapPercent = 15;

    /// <summary>The increase applies from the start of the third calendar month after receipt of the demand (§ 558b Abs. 1).</summary>
    public static DateOnly EffectiveFrom(DateOnly received) => new DateOnly(received.Year, received.Month, 1).AddMonths(3);

    /// <summary>The tenant may consent until the end of the second calendar month after receipt (§ 558b Abs. 2).</summary>
    public static DateOnly ConsentDeadline(DateOnly received) => EffectiveFrom(received).AddDays(-1);

    /// <summary>
    /// Earliest receipt of a demand. <paramref name="baseline"/> is the start of the lease or the day of the last increase;
    /// the one-year lockout only applies after an earlier increase.
    /// </summary>
    public static DateOnly EarliestDeclaration(DateOnly baseline, bool afterIncrease)
    {
        var waited = baseline.AddMonths(WaitMonths);
        var day = afterIncrease ? baseline.AddMonths(LockoutMonths) : baseline;
        while (EffectiveFrom(day) < waited)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>Highest rent allowed by the cap limit: rent three years ago plus 20 % (15 %), rounded down to the cent.</summary>
    public static decimal CapLimit(decimal rentThreeYearsAgo, int capPercent) => Math.Floor(rentThreeYearsAgo * (100 + capPercent)) / 100;

    /// <summary>Comparative rent of a flat from the rent index: per m² times the living area, commercially rounded.</summary>
    public static decimal FromPerSquareMetre(decimal perSquareMetre, decimal area) => Math.Round(perSquareMetre * area, 2, MidpointRounding.AwayFromZero);

    public static ComparativeRentAssessment Assess(
        DateOnly leaseStart,
        DateOnly? lastIncrease,
        decimal currentRent,
        decimal rentThreeYearsAgo,
        int capPercent,
        decimal? comparativeRent,
        DateOnly declarationReceived,
        decimal newRent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var baseline = lastIncrease ?? leaseStart;
        var earliest = EarliestDeclaration(baseline, lastIncrease is not null);
        var effective = EffectiveFrom(declarationReceived);
        var capLimit = CapLimit(rentThreeYearsAgo, capPercent);
        var max = comparativeRent is { } comparative ? Math.Min(comparative, capLimit) : capLimit;

        if (newRent <= currentRent)
        {
            errors.Add("Die neue Miete muss höher sein als die bisherige.");
        }

        if (lastIncrease is { } last && declarationReceived < last.AddMonths(LockoutMonths))
        {
            errors.Add($"Das Erhöhungsverlangen darf frühestens ein Jahr nach der letzten Mieterhöhung zugehen (letzte Erhöhung {last:dd.MM.yyyy}); frühestens möglich: {earliest:dd.MM.yyyy}.");
        }
        else if (effective < baseline.AddMonths(WaitMonths))
        {
            errors.Add($"Die Miete muss zum Zeitpunkt der Erhöhung seit 15 Monaten unverändert sein (seit {baseline:dd.MM.yyyy}); das Verlangen kann frühestens am {earliest:dd.MM.yyyy} zugehen.");
        }

        if (comparativeRent is { } cr && newRent > cr)
        {
            errors.Add($"Die neue Miete übersteigt die ortsübliche Vergleichsmiete von {Euro(cr)}.");
        }

        if (newRent > capLimit)
        {
            errors.Add($"Die Kappungsgrenze von {capPercent} % in {CapYears} Jahren erlaubt höchstens {Euro(capLimit)} (Miete vor drei Jahren {Euro(rentThreeYearsAgo)}).");
        }

        if (lastIncrease is null)
        {
            warnings.Add("Bei einer Neuvermietung in einem Gebiet mit Mietpreisbremse (§ 556d BGB) darf schon die Ausgangsmiete höchstens 10 % über der Vergleichsmiete liegen; sie ist die Grundlage der Kappungsgrenze.");
        }

        return new ComparativeRentAssessment(errors, warnings, earliest, effective, ConsentDeadline(declarationReceived), capLimit, max);
    }

    private static string Euro(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " €";
}
