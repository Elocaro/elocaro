using Elocaro.Domain.Common;
namespace Elocaro.Domain.Tax;

/// <summary>Acquisition data of a building that is the basis of the depreciation (AfA).</summary>
/// <param name="PurchaseDate">Date of acquisition (transfer of benefits and burdens).</param>
/// <param name="PurchasePrice">Purchase price including land.</param>
/// <param name="AncillaryCosts">Notary, land registry, transfer tax, broker (apportioned like the price).</param>
/// <param name="LandSharePercent">Share of land in the purchase price (0–100); not depreciable.</param>
/// <param name="CompletionYear">Year of completion of the building; decides the standard rate.</param>
/// <param name="RateOverridePercent">Manual rate, e.g. 100 / shorter remaining useful life from an expert opinion.</param>
/// <param name="SpecialRatePercent">Optional additional special depreciation (§ 7b EStG) per year.</param>
/// <param name="SpecialYears">Number of years in which the special depreciation applies (beginning in the acquisition year).</param>
public sealed record BuildingAcquisition(
    DateOnly PurchaseDate,
    decimal PurchasePrice,
    decimal AncillaryCosts,
    decimal LandSharePercent,
    int CompletionYear,
    decimal? RateOverridePercent = null,
    decimal? SpecialRatePercent = null,
    int SpecialYears = 4);

/// <summary>Subsequent cost of a building.</summary>
/// <param name="IsMaintenance">
/// <c>true</c> = renovation/modernisation measure that counts for the 15 % limit of the acquisition-related costs
/// (§ 6 para. 1 no. 1a EStG, net); <c>false</c> = capitalised production cost that is depreciated with the building.
/// </param>
public sealed record SubsequentCost(DateOnly Date, decimal Amount, string Description, bool IsMaintenance);

/// <summary>Movable asset (e.g. fitted kitchen) with its own useful life.</summary>
public sealed record MovableAsset(string Name, DateOnly AcquisitionDate, decimal Cost, int UsefulLifeYears);

/// <summary>One year of the depreciation plan.</summary>
public sealed record DepreciationYear(int Year, decimal Building, decimal Movables, decimal BookValue)
{
    public decimal Total => Building + Movables;
}

public sealed record DepreciationPlan(
    decimal BuildingBase,
    decimal LandValue,
    decimal RatePercent,
    IReadOnlyList<DepreciationYear> Years,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Straight-line depreciation of a rented building (§ 7 para. 4 EStG) and of movable assets: pro rata temporis in the
/// acquisition year (full months from the month of acquisition), plan over all years with book value.
/// Pure function without database access.
/// </summary>
public static class DepreciationCalculator
{
    /// <summary>Limit of acquisition-related production costs (§ 6 para. 1 no. 1a EStG).</summary>
    public const decimal AcquisitionRelatedLimit = 0.15m;

    /// <summary>Statutory rate: 3 % from completion 2023, 2.5 % before 1925, otherwise 2 %.</summary>
    public static decimal StandardRate(int completionYear) => completionYear switch
    {
        >= 2023 => 3m,
        < 1925 => 2.5m,
        _ => 2m,
    };

    public static decimal EffectiveRate(BuildingAcquisition acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        return acquisition.RateOverridePercent ?? StandardRate(acquisition.CompletionYear);
    }

    /// <summary>Months of the year for which depreciation applies when acquired on <paramref name="date"/> (15 July → July–December = 6 months).</summary>
    public static int MonthsInYear(DateOnly date) => 13 - date.Month;

    /// <summary>Building share of the purchase price plus apportioned ancillary costs.</summary>
    public static decimal BuildingBase(BuildingAcquisition acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var buildingShare = 1m - (acquisition.LandSharePercent / 100m);
        return Math.Round((acquisition.PurchasePrice + acquisition.AncillaryCosts) * buildingShare, 2, MidpointRounding.AwayFromZero);
    }

    public static DepreciationPlan Calculate(
        BuildingAcquisition acquisition,
        IReadOnlyList<SubsequentCost> subsequentCosts,
        IReadOnlyList<MovableAsset> movables,
        int lastYear)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentNullException.ThrowIfNull(subsequentCosts);
        ArgumentNullException.ThrowIfNull(movables);

        var rate = EffectiveRate(acquisition);
        var buildingBase = BuildingBase(acquisition);
        var landValue = Math.Round(acquisition.PurchasePrice * acquisition.LandSharePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var warnings = AcquisitionRelatedWarnings(acquisition, buildingBase, subsequentCosts);

        var firstYear = Math.Min(acquisition.PurchaseDate.Year, movables.Count == 0 ? int.MaxValue : movables.Min(m => m.AcquisitionDate.Year));
        var years = new List<DepreciationYear>();
        var buildingBook = 0m;
        var movablesBook = 0m;
        var buildingDepreciated = 0m;
        var movablesDepreciated = movables.ToDictionary(m => m, _ => 0m);

        for (var year = firstYear; year <= lastYear; year++)
        {
            // Building: base and subsequent production costs enter pro rata temporis
            var buildingAmount = 0m;
            var buildingCosts = 0m;
            if (year >= acquisition.PurchaseDate.Year)
            {
                buildingCosts += year == acquisition.PurchaseDate.Year ? buildingBase : 0m;
                buildingAmount += Annual(buildingBase, rate, acquisition.PurchaseDate, year);
                if (acquisition.SpecialRatePercent is > 0 && year < acquisition.PurchaseDate.Year + acquisition.SpecialYears)
                {
                    buildingAmount += Annual(buildingBase, acquisition.SpecialRatePercent.Value, acquisition.PurchaseDate, year);
                }

                foreach (var cost in subsequentCosts.Where(k => !k.IsMaintenance && k.Date.Year <= year))
                {
                    buildingCosts += cost.Date.Year == year ? cost.Amount : 0m;
                    buildingAmount += Annual(cost.Amount, rate, cost.Date, year);
                }
            }

            var totalCosts = buildingBase + subsequentCosts.Where(k => !k.IsMaintenance && k.Date.Year <= year).Sum(k => k.Amount);
            buildingAmount = year < acquisition.PurchaseDate.Year ? 0m : Math.Min(buildingAmount, totalCosts - buildingDepreciated);
            buildingAmount = Math.Max(0m, Math.Round(buildingAmount, 2, MidpointRounding.AwayFromZero));
            buildingDepreciated += buildingAmount;
            buildingBook = year < acquisition.PurchaseDate.Year ? 0m : totalCosts - buildingDepreciated;

            var movablesAmount = 0m;
            movablesBook = 0m;
            foreach (var asset in movables.Where(m => m.AcquisitionDate.Year <= year))
            {
                var yearly = Math.Round(Annual(asset.Cost, 100m / asset.UsefulLifeYears, asset.AcquisitionDate, year), 2, MidpointRounding.AwayFromZero);
                yearly = Math.Min(yearly, asset.Cost - movablesDepreciated[asset]);
                movablesDepreciated[asset] += yearly;
                movablesAmount += yearly;
                movablesBook += asset.Cost - movablesDepreciated[asset];
            }

            years.Add(new DepreciationYear(year, buildingAmount, movablesAmount, buildingBook + movablesBook));
        }

        return new DepreciationPlan(buildingBase, landValue, rate, years, warnings);
    }

    private static decimal Annual(decimal basis, decimal ratePercent, DateOnly start, int year)
    {
        if (year < start.Year)
        {
            return 0m;
        }

        var months = year == start.Year ? MonthsInYear(start) : 12;
        return basis * ratePercent / 100m * months / 12m;
    }

    private static List<string> AcquisitionRelatedWarnings(BuildingAcquisition acquisition, decimal buildingBase, IReadOnlyList<SubsequentCost> costs)
    {
        var limit = Math.Round(buildingBase * AcquisitionRelatedLimit, 2, MidpointRounding.AwayFromZero);
        var end = acquisition.PurchaseDate.AddYears(3);
        var inWindow = costs.Where(k => k.IsMaintenance && k.Date >= acquisition.PurchaseDate && k.Date <= end).Sum(k => k.Amount);
        var warnings = new List<string>();
        if (inWindow > limit)
        {
            warnings.Add(
                $"Anschaffungsnahe Herstellungskosten: Die Instandsetzungs- und Modernisierungskosten der ersten drei Jahre ({GermanText.Money(inWindow)}, netto) " +
                $"überschreiten 15 % der Gebäude-Anschaffungskosten ({GermanText.Money(limit)}). Sie sind dann nicht sofort abziehbar, sondern über die AfA zu verteilen (§ 6 Abs. 1 Nr. 1a EStG).");
        }
        else if (limit > 0 && inWindow > limit * 0.8m)
        {
            warnings.Add($"Die Instandsetzungskosten der ersten drei Jahre ({GermanText.Money(inWindow)}) liegen nahe an der 15-%-Grenze ({GermanText.Money(limit)}).");
        }

        return warnings;
    }
}
