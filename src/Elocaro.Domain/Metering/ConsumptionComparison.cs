using Elocaro.Domain.Common;

namespace Elocaro.Domain.Metering;

/// <summary>Remembers which monthly consumption information was mailed, so it goes out once per lease and month.</summary>
[NoAudit]
public sealed class ConsumptionInformationDispatch : Entity
{
    private ConsumptionInformationDispatch()
    {
    }

    public ConsumptionInformationDispatch(Guid leaseId, int year, int month, DateTimeOffset sentAt)
    {
        LeaseId = leaseId;
        Year = year;
        Month = month;
        SentAt = sentAt;
    }

    public Guid LeaseId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public DateTimeOffset SentAt { get; private set; }
}

/// <summary>Comparison values of the monthly consumption information (§ 6a HeizkostenV): previous month, same month of the year before, average user.</summary>
public static class ConsumptionComparison
{
    /// <summary>Fewer comparable units make the average meaningless (and could disclose a neighbour's consumption).</summary>
    public const int MinimumUnits = 3;

    /// <summary>Mean of the values per unit of reference (m², persons); <c>null</c> with fewer than <see cref="MinimumUnits"/> values.</summary>
    public static decimal? Average(IReadOnlyCollection<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Count < MinimumUnits ? null : Math.Round(values.Average(), 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>Change against a reference in percent (rounded to whole percent); <c>null</c> if either is missing or the reference is zero.</summary>
    public static decimal? ChangePercent(decimal? current, decimal? reference) =>
        current is { } value && reference is { } other && other != 0m ? Math.Round((value - other) / other * 100m, 0, MidpointRounding.AwayFromZero) : null;
}
