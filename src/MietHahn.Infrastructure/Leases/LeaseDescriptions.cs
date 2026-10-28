using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Leases;

/// <summary>Short texts for lists and statements: the rented unit (with property) and the tenants of a lease.</summary>
internal static class LeaseDescriptions
{
    public static async Task<Dictionary<Guid, (string RentalUnit, string Tenant)>> OfAsync(MietHahnDbContext db, List<Lease> leases, CancellationToken cancellationToken)
    {
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var names = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.DisplayName);
        var leaseIds = leases.Select(v => v.Id).ToList();
        var extras = (await db.Set<LeaseRoom>().AsNoTracking().Where(r => leaseIds.Contains(r.LeaseId)).ToListAsync(cancellationToken)).ToLookup(r => r.LeaseId, r => r.RoomId);
        return leases.ToDictionary(v => v.Id, v =>
        {
            var m = rentalUnits.GetValueOrDefault(v.RentalUnitId);
            return (m is null ? "?" : $"{m.Property}, {RentalUnit.LabelOf(v.RentalUnitId, extras[v.Id], rentalUnits)}", LeaseService.TenantText(v, names));
        });
    }
}
