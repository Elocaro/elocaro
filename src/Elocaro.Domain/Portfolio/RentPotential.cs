using System.Globalization;
using Elocaro.Domain.Leases;

namespace Elocaro.Domain.Portfolio;

/// <summary>What stops the rent from rising further.</summary>
public enum RentPotentialLimit
{
    /// <summary>The rent already reaches the comparative rent or more.</summary>
    NoPotential,

    /// <summary>The comparative rent is the limit: the flat is brought up to it.</summary>
    ComparativeRent,

    /// <summary>The cap limit is the limit: more is possible later, in steps.</summary>
    CapLimit,

    /// <summary>The comparison cannot be made (no living area, or no model for § 558).</summary>
    NotAssessable,
}

/// <summary>Data of one lease for the rent potential.</summary>
/// <param name="CurrentRent">Base rent today.</param>
/// <param name="LivingArea">Area for the comparison with the rent index; <c>null</c> if not recorded.</param>
/// <param name="RentThreeYearsAgo">Base rent three years before the increase takes effect (basis of the cap limit).</param>
/// <param name="EarliestDeclaration">Earliest receipt of a demand (waiting periods of § 558 Abs. 1).</param>
/// <param name="AssessableUnderSection558">Standard rent only: index and graduated rents follow other rules.</param>
public sealed record RentPotentialInput(
    Guid LeaseId,
    string Unit,
    decimal CurrentRent,
    decimal? LivingArea,
    decimal RentThreeYearsAgo,
    int CapPercent,
    DateOnly EarliestDeclaration,
    bool AssessableUnderSection558);

/// <param name="Potential">Possible increase per month now; 0 if there is none.</param>
/// <param name="EffectiveFrom">Day the increase takes effect if the demand is received at the earliest day.</param>
public sealed record RentPotentialRow(
    Guid LeaseId,
    string Unit,
    decimal CurrentRent,
    decimal? LivingArea,
    decimal? CurrentPerSquareMetre,
    decimal? ComparativeRent,
    decimal? CapLimit,
    decimal? MaxRent,
    decimal Potential,
    RentPotentialLimit Limit,
    DateOnly? EarliestDeclaration,
    DateOnly? EffectiveFrom,
    string Calculation);

/// <summary>Rent potential of a property: the rent of each lease against the comparative rent from the rent index, within the cap limit (§ 558 BGB).</summary>
public static class RentPotentialCalculator
{
    public static RentPotentialRow Assess(RentPotentialInput input, decimal perSquareMetre, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.AssessableUnderSection558)
        {
            return NotAssessable(input, "Index- und Staffelmieten folgen eigenen Regeln (§§ 557a, 557b), kein Vergleich mit dem Mietspiegel.");
        }

        if (input.LivingArea is not { } area || area <= 0)
        {
            return NotAssessable(input, "Wohnfläche fehlt: an der Einheit erfassen.");
        }

        var comparative = ComparativeRent.FromPerSquareMetre(perSquareMetre, area);
        var capLimit = ComparativeRent.CapLimit(input.RentThreeYearsAgo, input.CapPercent);
        var max = Math.Min(comparative, capLimit);
        var potential = Math.Max(0m, max - input.CurrentRent);
        var limit = comparative <= input.CurrentRent
            ? RentPotentialLimit.NoPotential
            : capLimit < comparative ? RentPotentialLimit.CapLimit : RentPotentialLimit.ComparativeRent;
        // A demand cannot reach the tenant before today
        var earliest = input.EarliestDeclaration < today ? today : input.EarliestDeclaration;
        var german = CultureInfo.GetCultureInfo("de-DE");
        var calculation =
            $"Vergleichsmiete {perSquareMetre.ToString("N2", german)} €/m² × {area.ToString("0.##", german)} m² = {comparative.ToString("N2", german)} €; " +
            $"Kappungsgrenze {input.RentThreeYearsAgo.ToString("N2", german)} € + {input.CapPercent} % = {capLimit.ToString("N2", german)} €; " +
            $"zulässig höchstens {max.ToString("N2", german)} € − bisher {input.CurrentRent.ToString("N2", german)} €.";
        return new RentPotentialRow(
            input.LeaseId,
            input.Unit,
            input.CurrentRent,
            area,
            Math.Round(input.CurrentRent / area, 2, MidpointRounding.AwayFromZero),
            comparative,
            capLimit,
            max,
            potential,
            limit,
            earliest,
            ComparativeRent.EffectiveFrom(earliest),
            calculation);
    }

    private static RentPotentialRow NotAssessable(RentPotentialInput input, string reason) =>
        new(input.LeaseId, input.Unit, input.CurrentRent, input.LivingArea, null, null, null, null, 0m, RentPotentialLimit.NotAssessable, null, null, reason);
}

/// <summary>Rent potential of one property on one day.</summary>
/// <param name="PerSquareMetre">Comparative rent per m² from the rent index; <c>null</c> if not entered (then there are no rows).</param>
/// <param name="TotalPotential">Possible increase per month over all leases that can be assessed.</param>
public sealed record RentPotential(decimal? PerSquareMetre, IReadOnlyList<RentPotentialRow> Rows, decimal TotalPotential);
