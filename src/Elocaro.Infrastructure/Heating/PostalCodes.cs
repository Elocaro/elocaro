using System.Globalization;
using Elocaro.Infrastructure.Templates;

namespace Elocaro.Infrastructure.Heating;

/// <summary>
/// Position of a German postal code (mean of its places), from the bundled table <c>postal-codes.csv</c> (GeoNames, CC BY 4.0). Offline, so no address leaves
/// the installation. Used to find the nearest weather station.
/// </summary>
internal static class PostalCodes
{
    private static readonly Lazy<Dictionary<string, (double Latitude, double Longitude)>> Table = new(Load);

    public static (double Latitude, double Longitude)? Find(string? postalCode) =>
        postalCode is { Length: 5 } && Table.Value.TryGetValue(postalCode, out var position) ? position : null;

    /// <summary>Great-circle distance in kilometres (haversine).</summary>
    public static double DistanceKm((double Latitude, double Longitude) a, (double Latitude, double Longitude) b)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var (dLat, dLon) = (Rad(b.Latitude - a.Latitude), Rad(b.Longitude - a.Longitude));
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(h));
    }

    private static Dictionary<string, (double, double)> Load()
    {
        var table = new Dictionary<string, (double, double)>();
        foreach (var line in EmbeddedResource.ReadText("Heating/postal-codes.csv").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(';');
            if (fields.Length == 3
                && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                && double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                table[fields[0]] = (latitude, longitude);
            }
        }

        return table;
    }
}
