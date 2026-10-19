using System.Globalization;
using MietHahn.Domain.Common;

namespace MietHahn.Domain.Properties;

public enum HeatingType
{
    Unknown = 0,
    CentralHeating = 1,
    FloorHeating = 2,
    DistrictHeating = 3,
    HeatPump = 4,
    IndividualStoves = 5,
    Other = 9,
}

public enum EnergySource
{
    Unknown = 0,
    NaturalGas = 1,
    HeatingOil = 2,
    DistrictHeating = 3,
    Electricity = 4,
    Wood = 5,
    LiquefiedGas = 6,
    Other = 9,
}

/// <summary>
/// Ein Haus bzw. Grundstück. Aggregatwurzel für <see cref="Unit"/>en und deren <see cref="Room"/>.
/// Flächen sind zeitabhängig (<see cref="PropertyArea"/>), damit Abrechnungen nach einem Umbau stimmen.
/// </summary>
public sealed class Property : Entity
{
    private Property()
    {
        Name = "";
        Street = "";
        PostalCode = "";
        City = "";
    }

    public Property(string name, string street, string postalCode, string city)
    {
        Name = name;
        Street = street;
        PostalCode = postalCode;
        City = city;
        Validate();
    }

    /// <summary>Kurzname, z. B. „Hauptstraße 5“ oder „MFH Lindenweg“.</summary>
    public string Name { get; set; }

    public string Street { get; set; }

    public string PostalCode { get; set; }

    public string City { get; set; }

    public int? YearBuilt { get; set; }

    public string? CadastralDistrict { get; set; }

    public string? ParcelNumber { get; set; }

    public string? PropertyTaxReference { get; set; }

    public HeatingType HeatingType { get; set; }

    public EnergySource EnergySource { get; set; }

    /// <summary>Foto des Hauses aus der Dateiablage.</summary>
    public Guid? ImageFileId { get; set; }

    public string? Notes { get; set; }

    public List<Unit> Units { get; private set; } = [];

    public List<PropertyArea> Areas { get; private set; } = [];

    public string Address => $"{Street}, {PostalCode} {City}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Street);
        ArgumentException.ThrowIfNullOrWhiteSpace(PostalCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(City);
    }

    public Unit AddUnit(string name, UnitType type)
    {
        if (Units.Any(e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new RuleViolationException($"Im Objekt gibt es bereits eine Einheit „{name.Trim()}“.");
        }

        var unit = new Unit(Id, name.Trim(), type, Units.Count == 0 ? 1 : Units.Max(e => e.Order) + 1);
        Units.Add(unit);
        return unit;
    }

    /// <summary>Gesamt- und Gemeinschaftsfläche ab dem Stichtag (Wohnfläche des Hauses bzw. Treppenhaus, Keller …).</summary>
    public PropertyArea SetArea(DateOnly from, decimal totalArea, decimal commonArea) =>
        TimeSeries.CarryForward(Areas, from, g => new PropertyArea(Id, g, totalArea, commonArea));

    public PropertyArea? AreaOn(DateOnly day) => TimeSeries.At(Areas, day);

    /// <summary>Plausibilitätshinweise zum Stichtag (keine Fehler: Daten dürfen unvollständig sein).</summary>
    public IReadOnlyList<string> ValidationHints(DateOnly day)
    {
        var hints = new List<string>();
        var area = AreaOn(day);
        var unitsTotal = Units.Sum(e => e.AreaOn(day)?.LivingArea ?? 0);
        if (area is not null && unitsTotal + area.CommonArea > area.TotalArea)
        {
            hints.Add(
                $"Die Wohnflächen der Einheiten ({SquareMeters(unitsTotal)}) und die Gemeinschaftsfläche ({SquareMeters(area.CommonArea)}) " +
                $"sind zusammen größer als die Gesamtfläche des Objekts ({SquareMeters(area.TotalArea)}).");
        }

        hints.AddRange(Units.SelectMany(e => e.ValidationHints(day)));
        return hints;
    }

    internal static string SquareMeters(decimal value) => value.ToString("#,##0.##", CultureInfo.GetCultureInfo("de-DE")) + " m²";
}

/// <summary>Flächen eines Objekts in einem Zeitraum.</summary>
public sealed class PropertyArea : Entity, ITimeDependent
{
    private PropertyArea()
    {
    }

    internal PropertyArea(Guid propertyId, Period validity, decimal totalArea, decimal commonArea)
    {
        AreaValue.Validate(totalArea, "Gesamtfläche");
        AreaValue.Validate(commonArea, "Gemeinschaftsfläche", allowNull: true);
        PropertyId = propertyId;
        Validity = validity;
        TotalArea = totalArea;
        CommonArea = commonArea;
    }

    public Guid PropertyId { get; private set; }

    public Period Validity { get; private set; }

    /// <summary>Summe aller Wohn-/Nutzflächen des Hauses in m².</summary>
    public decimal TotalArea { get; private set; }

    /// <summary>Gemeinschaftlich genutzte Flächen (Treppenhaus, Waschküche …) in m².</summary>
    public decimal CommonArea { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);
}

internal static class AreaValue
{
    public static void Validate(decimal value, string name, bool allowNull = false)
    {
        if (value < 0 || (!allowNull && value == 0))
        {
            throw new RuleViolationException($"Die {name} muss größer als 0 sein.");
        }

        if (value > 100_000)
        {
            throw new RuleViolationException($"Die {name} ist unplausibel groß.");
        }

        if (decimal.Round(value, 2) != value)
        {
            throw new RuleViolationException($"Die {name} hat höchstens zwei Nachkommastellen.");
        }
    }
}
