using Elocaro.Application.Documents;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Leases;

internal sealed class DocumentLeaseSource(ElocaroDbContext db) : IDocumentLeaseSource
{
    public IQueryable<Guid> LeaseIdsOfUnits(IQueryable<Guid> unitIds) =>
        db.Set<Lease>().Where(v => unitIds.Contains(v.UnitId)).Select(v => v.Id);

    public IQueryable<Guid> LeaseIdsOfUnit(Guid unitId) =>
        db.Set<Lease>().Where(v => v.UnitId == unitId).Select(v => v.Id);

    public IQueryable<Guid> LeaseIdsOfPerson(Guid personId) =>
        db.Set<LeaseParty>().Where(p => p.PersonId == personId).Select(p => p.LeaseId);

    public TenantLeaseScope TenantScopeOf(IReadOnlyCollection<Guid> personIds, DateOnly today)
    {
        var leases = db.Set<Lease>().Where(v => v.Parties.Any(p => personIds.Contains(p.PersonId) && p.Role == PartyRole.Tenant));
        var current = leases.Where(v => v.Term.From <= today && (v.Term.To == null || v.Term.To >= today));
        return new TenantLeaseScope(leases.Select(v => v.Id), current.Select(v => v.UnitId));
    }

    public async Task<bool> LeaseExistsAsync(Guid leaseId, CancellationToken cancellationToken = default) =>
        await db.Set<Lease>().AnyAsync(v => v.Id == leaseId, cancellationToken);

    public async Task<IReadOnlyList<ReferenceOption>> UnitOptionsAsync(CancellationToken cancellationToken = default)
    {
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        return rentalUnits.Values.Where(m => m.RoomId is null).Select(m => new ReferenceOption(m.Id, $"{m.Name} ({m.Property})")).ToList();
    }

    public async Task<IReadOnlyList<ReferenceOption>> LeaseOptionsAsync(CancellationToken cancellationToken = default)
    {
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).ToListAsync(cancellationToken);
        var people = await db.Set<Person>().AsNoTracking().Where(p => p.AnonymizedAt == null).ToListAsync(cancellationToken);
        var names = people.ToDictionary(p => p.Id, p => p.DisplayName);
        return leases.OrderByDescending(v => v.Start).Select(v => new ReferenceOption(v.Id,
            $"{rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Name}: {LeaseService.TenantText(v, names)} (ab {v.Start:dd.MM.yyyy})")).ToList();
    }

    public async Task<DocumentReferenceNames> NamesAsync(IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken = default)
    {
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var unitOfLease = await db.Set<Lease>().AsNoTracking().Where(v => leaseIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.RentalUnitId, cancellationToken);
        return new DocumentReferenceNames(rentalUnits.ToDictionary(m => m.Key, m => m.Value.Name), unitOfLease);
    }
}
