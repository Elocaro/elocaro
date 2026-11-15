using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.Portfolio;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Portfolio;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Portfolio;

internal sealed class RentPotentialService(ElocaroDbContext db, IRentIncreases increases, TimeProvider clock) : IRentPotential
{
    public async Task<RentPotential?> GetAsync(ClaimsPrincipal user, Guid propertyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return null;
        }

        var property = await db.Set<Property>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == propertyId, cancellationToken);
        if (property is null)
        {
            return null;
        }

        if (property.RentIndexPerSquareMetre is not { } perSquareMetre)
        {
            return new RentPotential(null, [], 0m);
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking()
            .Where(v => db.Set<Unit>().Any(u => u.Id == v.UnitId && u.PropertyId == propertyId))
            .ToListAsync(cancellationToken);
        var rows = new List<RentPotentialRow>();
        foreach (var lease in leases.Where(v => v.Term.Contains(today)))
        {
            if (await increases.OverviewAsync(lease.Id, cancellationToken) is not { BaseRent: { } rent } overview)
            {
                continue;
            }

            rows.Add(RentPotentialCalculator.Assess(
                new RentPotentialInput(
                    lease.Id, overview.RentalUnit, rent, overview.LivingArea, overview.RentThreeYearsAgo ?? rent, overview.CapPercent,
                    overview.EarliestDeclaration, overview.Model == RentModel.Standard),
                perSquareMetre,
                today));
        }

        rows = [.. rows.OrderByDescending(r => r.Potential).ThenBy(r => r.Unit, StringComparer.CurrentCultureIgnoreCase)];
        return new RentPotential(perSquareMetre, rows, rows.Sum(r => r.Potential));
    }
}
