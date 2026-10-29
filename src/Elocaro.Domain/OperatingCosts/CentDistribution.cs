namespace Elocaro.Domain.OperatingCosts;

/// <summary>
/// Distribution of an amount in cents proportionally to weights so that the parts add up to the total exactly
/// (largest remainder method): every part is rounded down to a cent first, the remaining cents go to the parts
/// with the largest fractional remainders (equal remainders, compared to 10 decimal places: lower position first). Deterministic.
/// </summary>
public static class CentDistribution
{
    public static decimal[] Distribute(decimal total, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var result = new decimal[weights.Count];
        var weightSum = weights.Sum();
        if (weights.Count == 0 || weightSum == 0)
        {
            return result;
        }

        var cents = decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var exact = weights.Select(w => cents * w / weightSum).ToArray();
        var floors = exact.Select(decimal.Floor).ToArray();
        var remainder = (int)(cents - floors.Sum());
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => decimal.Round(exact[i] - floors[i], 10))
            .ThenBy(i => i)
            .Take(remainder)
            .ToHashSet();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (floors[i] + (order.Contains(i) ? 1m : 0m)) / 100m;
        }

        return result;
    }

    /// <summary>Commercial rounding to cents.</summary>
    public static decimal Cents(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
