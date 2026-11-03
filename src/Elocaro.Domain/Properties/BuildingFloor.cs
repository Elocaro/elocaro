namespace Elocaro.Domain.Properties;

/// <summary>Floors of a building as numbers: 0 ground floor, 1 and up upper floors, negative basement, <see cref="Attic"/> under the roof.</summary>
public static class BuildingFloor
{
    public const int Attic = 100;

    /// <summary>The floors offered for selection, from the roof down.</summary>
    public static IReadOnlyList<int> Choices { get; } = [Attic, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, -1, -2];

    /// <summary>The floor of a unit: as entered, otherwise read from the free text of its location; <c>null</c> if unknown.</summary>
    public static int? Of(int? floor, string? location) => floor ?? Parse(location);

    /// <summary>Reads the floor from a location such as "EG links", "2. OG" or "Dachgeschoss".</summary>
    public static int? Parse(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var text = location.Trim().ToLowerInvariant();
        var digits = new string([.. text.Where(char.IsDigit)]);
        var number = int.TryParse(digits, out var n) ? n : 0;
        if (text.Contains("dach", StringComparison.Ordinal) || text.StartsWith("dg", StringComparison.Ordinal))
        {
            return Attic;
        }

        if (text.Contains("unter", StringComparison.Ordinal) || text.Contains("keller", StringComparison.Ordinal) || text.StartsWith("ug", StringComparison.Ordinal) || text.Contains(" ug", StringComparison.Ordinal))
        {
            return -Math.Max(1, number);
        }

        if (text.Contains("erd", StringComparison.Ordinal) || text.StartsWith("eg", StringComparison.Ordinal))
        {
            return 0;
        }

        return text.Contains("ober", StringComparison.Ordinal) || text.Contains("og", StringComparison.Ordinal) || number > 0
            ? Math.Clamp(number, 1, Attic - 1)
            : null;
    }

    /// <summary>The short name, e.g. "EG", "2. OG", "1. UG", "DG".</summary>
    public static string Label(int floor) => floor switch
    {
        Attic => "DG",
        0 => "EG",
        > 0 => $"{floor}. OG",
        _ => $"{-floor}. UG",
    };
}
