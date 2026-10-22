using System.Security.Claims;
using MietHahn.Application.Tax;

namespace MietHahn.Application.Portal;

/// <summary>
/// Read-only view for owners (role "Eigentümer"): the properties they own and their share of the Anlage V.
/// Everything is derived from the signed-in account; other properties are simply not found.
/// </summary>
public interface IOwnerPortal
{
    /// <summary>The properties the account's person owns or owned (current ones first). Others: empty.</summary>
    Task<IReadOnlyList<OwnedPropertyView>> PropertiesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>The share of the Anlage V; <c>null</c> if the person did not own the property in that year.</summary>
    Task<AnlageVReport?> AnlageVAsync(ClaimsPrincipal user, Guid propertyId, int year, bool surplusForecastPositive = false, CancellationToken cancellationToken = default);
}

public sealed record OwnedPropertyView(Guid PropertyId, string Name, string Address, decimal SharePercent, DateOnly? Since, DateOnly? Until, bool Current);
