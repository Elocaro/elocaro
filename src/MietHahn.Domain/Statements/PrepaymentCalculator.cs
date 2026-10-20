namespace MietHahn.Domain.Statements;

/// <summary>Proposal for the new monthly prepayment after a statement: annual costs ÷ 12 plus a safety margin, rounded up to whole euros.</summary>
public static class PrepaymentCalculator
{
    public const decimal DefaultSafetyPercent = 5m;

    /// <param name="share">Costs of the tenant in the billing period.</param>
    /// <param name="tenantDays">Days of the tenancy in the period (a part of the year is extrapolated to twelve months).</param>
    /// <param name="periodDays">Days of the billing period.</param>
    public static decimal Propose(decimal share, int tenantDays, int periodDays, decimal safetyPercent)
    {
        if (tenantDays <= 0 || periodDays <= 0)
        {
            return 0m;
        }

        var annual = share / tenantDays * periodDays;
        var monthly = annual / 12m * (1m + Math.Max(0m, safetyPercent) / 100m);
        return Math.Max(0m, Math.Ceiling(monthly));
    }

    /// <summary>First day of the month after the next one: the declaration should arrive before the change takes effect.</summary>
    public static DateOnly DefaultEffectiveDate(DateOnly sentOn) => new DateOnly(sentOn.Year, sentOn.Month, 1).AddMonths(2);
}
