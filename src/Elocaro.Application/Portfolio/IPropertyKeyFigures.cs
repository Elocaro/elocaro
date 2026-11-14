using System.Security.Claims;
using Elocaro.Domain.Portfolio;

namespace Elocaro.Application.Portfolio;

/// <summary>Key figures of one property and year for decisions (rent increase, renovation, financing), with the way they are calculated.</summary>
public interface IPropertyKeyFigures
{
    /// <summary>
    /// The figures, or <c>null</c> for users without finance rights and for an unknown property. Income and costs are those of the
    /// Anlage V (cash basis), so the figures agree with it.
    /// </summary>
    Task<PropertyKeyFigures?> GetAsync(ClaimsPrincipal user, Guid propertyId, int year, CancellationToken cancellationToken = default);
}
