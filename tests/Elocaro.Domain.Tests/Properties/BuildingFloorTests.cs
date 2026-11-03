using Elocaro.Domain.Properties;

namespace Elocaro.Domain.Tests.Properties;

public class BuildingFloorTests
{
    [Theory]
    [InlineData("EG links", 0)]
    [InlineData("EG rechts", 0)]
    [InlineData("Erdgeschoss", 0)]
    [InlineData("1. OG", 1)]
    [InlineData("2. Obergeschoss", 2)]
    [InlineData("OG", 1)]
    [InlineData("DG", BuildingFloor.Attic)]
    [InlineData("Dachgeschoss", BuildingFloor.Attic)]
    [InlineData("Keller", -1)]
    [InlineData("2. UG", -2)]
    public void The_floor_is_read_from_the_location(string location, int expected) =>
        Assert.Equal(expected, BuildingFloor.Parse(location));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("links")]
    public void A_location_without_a_floor_stays_unknown(string? location) =>
        Assert.Null(BuildingFloor.Parse(location));

    [Fact]
    public void An_entered_floor_wins_over_the_location() =>
        Assert.Equal(3, BuildingFloor.Of(3, "EG links"));

    [Theory]
    [InlineData(BuildingFloor.Attic, "DG")]
    [InlineData(2, "2. OG")]
    [InlineData(0, "EG")]
    [InlineData(-1, "1. UG")]
    public void Floors_have_short_names(int floor, string expected) =>
        Assert.Equal(expected, BuildingFloor.Label(floor));
}
