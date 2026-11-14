using System.Security.Claims;
using Elocaro.Domain.Portfolio;

namespace Elocaro.Application.Portfolio;

/// <summary>Rent potential of a property: the rents of the running leases against the comparative rent from the rent index, with cap limit and earliest date.</summary>
public interface IRentPotential
{
    /// <summary>The potential today, or <c>null</c> for users without finance rights and for an unknown property.</summary>
    Task<RentPotential?> GetAsync(ClaimsPrincipal user, Guid propertyId, CancellationToken cancellationToken = default);
}
