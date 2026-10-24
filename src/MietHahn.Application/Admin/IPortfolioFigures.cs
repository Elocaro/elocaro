using System.Security.Claims;

namespace MietHahn.Application.Admin;

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
public sealed record PortfolioFigures(DateOnly Month, int Units, int Occupied, decimal MonthlyRent, decimal PaidThisMonth, decimal Arrears, int ArrearsLeases)
{
    public int Vacant => Units - Occupied;
}
