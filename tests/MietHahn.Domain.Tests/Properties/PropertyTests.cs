using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;

namespace MietHahn.Domain.Tests.Properties;

public class PropertyTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void Apartment_building_with_four_flats_one_a_shared_flat_with_four_rooms()
    {
        var house = SampleHouse();

        Assert.Equal(4, house.Units.Count);
        var sharedFlat = house.Units.Single(e => e.IsSharedFlat);
        Assert.Equal(4, sharedFlat.Room.Count);
        Assert.False(sharedFlat.IsSelfRentable);
        Assert.Equal(3, house.Units.Count(e => e.IsSelfRentable));
        Assert.Equal([1, 2, 3, 4], house.Units.Select(e => e.Order));
        Assert.Empty(house.ValidationHints(Start));
    }

    [Fact]
    public void Area_after_renovation_applies_from_effective_date_old_one_is_kept()
    {
        var unit = SampleHouse().Units[0];

        unit.SetArea(new DateOnly(2026, 7, 1), 82.4m, 80m);

        Assert.Equal(75.5m, unit.AreaOn(new DateOnly(2026, 6, 30))!.LivingArea);
        Assert.Equal(82.4m, unit.AreaOn(new DateOnly(2026, 7, 1))!.LivingArea);
        Assert.Equal(80m, unit.AreaOn(new DateOnly(2026, 7, 1))!.HeatedArea);
        Assert.Equal(75.5m, unit.AreaOn(Start)!.HeatedArea);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(12.345)]
    public void Implausible_living_area_is_rejected(double value)
    {
        var unit = new Property("Test", "Weg 1", "12345", "Ort").AddUnit("W1", UnitType.Apartment);

        Assert.Throws<RuleViolationException>(() => unit.SetArea(Start, (decimal)value));
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        var house = SampleHouse();

        Assert.Throws<RuleViolationException>(() => house.AddUnit("wohnung 1", UnitType.Apartment));
        Assert.Throws<RuleViolationException>(() => house.Units.Single(e => e.IsSharedFlat).AddRoom("Zimmer 2"));
    }

    [Fact]
    public void Sum_of_unit_areas_larger_than_total_area_gives_hint()
    {
        var house = SampleHouse();

        house.SetArea(Start, 250m, 20m);

        var hint = Assert.Single(house.ValidationHints(Start));
        Assert.Contains("größer als die Gesamtfläche des Objekts (250 m²)", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_areas_larger_than_shared_flat_living_area_gives_hint()
    {
        var house = SampleHouse();
        var sharedFlat = house.Units.Single(e => e.IsSharedFlat);

        sharedFlat.Room[0].SetArea(Start, 40m, 20m);

        Assert.Contains(house.ValidationHints(Start), h => h.StartsWith("Wohnung 4: Die Zimmerflächen", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_living_area_produces_hint()
    {
        var house = new Property("Test", "Weg 1", "12345", "Ort");
        house.AddUnit("W1", UnitType.Apartment);

        Assert.Equal("W1: Keine Wohnfläche zum 01.01.2026 hinterlegt.", Assert.Single(house.ValidationHints(Start)));
    }

    [Fact]
    public void Allocable_area_of_a_room_includes_the_common_share()
    {
        var room = SampleHouse().Units.Single(e => e.IsSharedFlat).Room[0];

        Assert.Equal(14m + 8.5m, room.AreaOn(Start)!.AllocationArea);
    }

    /// <summary>Das Haus aus dem Interview: MFH mit 4 Wohnungen, Wohnung 4 ist eine WG mit 4 Zimmern.</summary>
    internal static Property SampleHouse()
    {
        var house = new Property("Lindenweg 3", "Lindenweg 3", "50667", "Köln") { YearBuilt = 1965, HeatingType = HeatingType.CentralHeating, EnergySource = EnergySource.NaturalGas };
        house.SetArea(Start, 400m, 30m);
        var areas = new[] { 75.5m, 68m, 92m, 110m };
        for (var i = 0; i < 4; i++)
        {
            var unit = house.AddUnit($"Wohnung {i + 1}", UnitType.Apartment);
            unit.SetArea(Start, areas[i]);
        }

        var sharedFlat = house.Units[3];
        foreach (var (name, area) in new[] { ("Zimmer 1", 14m), ("Zimmer 2", 16m), ("Zimmer 3", 12m), ("Zimmer 4", 18m) })
        {
            sharedFlat.AddRoom(name).SetArea(Start, area, 8.5m);
        }

        return house;
    }
}
