namespace MietHahn.Domain.Tax;

/// <summary>Deduction of one year of a distributed maintenance expense with the amount still to be deducted afterwards.</summary>
public sealed record MaintenanceInstalment(int Year, decimal Amount, decimal Remaining);

/// <summary>Distribution of maintenance expenses over several years (§ 82b EStDV).</summary>
public static class MaintenanceCalculator
{
    /// <summary>Even instalments; the rounding remainder goes to the first year.</summary>
    public static IReadOnlyList<MaintenanceInstalment> Schedule(decimal amount, int startYear, int years)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(years, 1);
        var share = Math.Round(amount / years, 2, MidpointRounding.ToZero);
        var remaining = amount;
        var result = new List<MaintenanceInstalment>(years);
        for (var i = 0; i < years; i++)
        {
            var instalment = i == 0 ? amount - (share * (years - 1)) : share;
            remaining -= instalment;
            result.Add(new MaintenanceInstalment(startYear + i, instalment, remaining));
        }

        return result;
    }
}
