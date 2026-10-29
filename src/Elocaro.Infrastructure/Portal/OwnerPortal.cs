using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Portal;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Portal;

internal sealed class OwnerPortal(ElocaroDbContext db, IAnlageVService anlageV, TimeProvider clock) : IOwnerPortal
{
    public async Task<IReadOnlyList<OwnedPropertyView>> PropertiesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var persons = await PersonsAsync(user, cancellationToken);
        if (persons.Count == 0)
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var owners = await db.Set<PropertyOwner>().AsNoTracking().Where(o => persons.Contains(o.PersonId)).ToListAsync(cancellationToken);
        var ids = owners.Select(o => o.PropertyId).Distinct().ToList();
        var properties = await db.Set<Property>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return owners
            .Where(o => properties.ContainsKey(o.PropertyId))
            .Select(o => new OwnedPropertyView(o.PropertyId, properties[o.PropertyId].Name, properties[o.PropertyId].Address, o.SharePercent, o.ValidFrom, o.ValidTo, o.IsValidOn(today)))
            .OrderByDescending(v => v.Current).ThenBy(v => v.Name, StringComparer.CurrentCulture).ThenBy(v => v.Since)
            .ToList();
    }

    public async Task<AnlageVReport?> AnlageVAsync(ClaimsPrincipal user, Guid propertyId, int year, bool surplusForecastPositive = false, CancellationToken cancellationToken = default)
    {
        foreach (var person in await PersonsAsync(user, cancellationToken))
        {
            if (await anlageV.GetForOwnerAsync(propertyId, year, person, surplusForecastPositive, cancellationToken) is { } report)
            {
                return report;
            }
        }

        return null;
    }

    /// <summary>The persons linked to the account of an owner; empty for any other account.</summary>
    private async Task<List<Guid>> PersonsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Owner) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return [];
        }

        return await db.Set<Person>().AsNoTracking().Where(p => p.UserId == userId && p.AnonymizedAt == null).Select(p => p.Id).ToListAsync(cancellationToken);
    }
}
