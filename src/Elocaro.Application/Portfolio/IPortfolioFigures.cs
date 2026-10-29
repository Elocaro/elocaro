using System.Security.Claims;

namespace Elocaro.Application.Portfolio;

/// <summary>The few numbers a landlord wants at a glance on the start page.</summary>
public interface IPortfolioFigures
{
    /// <summary>
    /// The figures, or <c>null</c> for users without finance rights and while there is nothing rentable yet.
    /// </summary>
    Task<PortfolioFigures?> FiguresAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

/// <param name="Units">Rentable units (apartments without rooms and shared-flat rooms).</param>
/// <param name="Occupied">Of them let today.</param>
/// <param name="MonthlyRent">Total monthly rent incl. prepayments of the running leases.</param>
/// <param name="PaidThisMonth">Payments received in the current month.</param>
/// <param name="Arrears">Due claims not yet paid.</param>
/// <param name="ArrearsLeases">Number of tenancies with arrears.</param>
/// <summary>One month of the history: claims due, payments received and the open balance at the end of the month.</summary>
public sealed record MonthFigure(DateOnly Month, decimal Due, decimal Paid, decimal OpenAtEnd);

/// <summary>Costs of the receipts per billing year.</summary>
public sealed record YearCost(int Year, decimal Amount);

public sealed record PortfolioFigures(
    DateOnly Month,
    int Units,
    int Occupied,
    decimal MonthlyRent,
    decimal PaidThisMonth,
    decimal Arrears,
    int ArrearsLeases,
    IReadOnlyList<MonthFigure>? History = null,
    IReadOnlyList<YearCost>? Costs = null)
{
    public int Vacant => Units - Occupied;
}
