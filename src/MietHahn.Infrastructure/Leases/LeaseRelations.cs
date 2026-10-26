using Microsoft.EntityFrameworkCore;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Leases;

/// <summary>Who rented a rental object on a given day (for the property overview and vacancy).</summary>
internal sealed class LeaseOccupancy(MietHahnDbContext db) : IOccupancyLookup
{
    public async Task<IReadOnlyDictionary<Guid, Occupancy>> AtAsync(IReadOnlyCollection<Guid> rentalUnitIds, DateOnly day, CancellationToken cancellationToken = default)
    {
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.ExtraRooms)
            .Where(v => (rentalUnitIds.Contains(v.RentalUnitId) || v.ExtraRooms.Any(r => rentalUnitIds.Contains(r.RoomId))) && v.Term.From <= day && (v.Term.To == null || v.Term.To >= day))
            .ToListAsync(cancellationToken);
        var personIds = leases.SelectMany(v => v.Parties.Select(p => p.PersonId)).Distinct().ToList();
        var names = (await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => p.DisplayName);
        var result = new Dictionary<Guid, Occupancy>();
        foreach (var lease in leases)
        {
            var occupancy = new Occupancy(lease.Id, LeaseService.TenantText(lease, names), lease.Start, lease.End);
            foreach (var id in lease.ExtraRooms.Select(r => r.RoomId).Prepend(lease.RentalUnitId))
            {
                result[id] = occupancy;
            }
        }

        return result;
    }

    public Task<bool> HasLeasesAsync(Guid rentalUnitId, CancellationToken cancellationToken = default) =>
        db.Set<Lease>().AnyAsync(v => v.RentalUnitId == rentalUnitId, cancellationToken);
}

/// <summary>Tenants and guarantors from the leases: role display, deletion lock and retention period.</summary>
internal sealed class LeasePersonalReferences(MietHahnDbContext db, TimeProvider clock) : IPersonReference
{
    public async Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties)
            .Where(v => v.Parties.Any(p => personIds.Contains(p.PersonId)))
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return [];
        }

        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        return leases.SelectMany(v => v.Parties.Where(p => personIds.Contains(p.PersonId)).Select(p =>
        {
            var rentalUnit = rentalUnits.GetValueOrDefault(v.RentalUnitId);
            return new PersonRelation(
                p.PersonId,
                p.Role == PartyRole.Tenant ? "Mieter" : "Bürge",
                $"{rentalUnit?.Name}, {rentalUnit?.Property} ({v.Term})",
                v.End is null || v.End >= today,
                v.End,
                $"/verwaltung/vertraege/{v.Id}");
        })).ToList();
    }
}

/// <summary>Leases of the person for the GDPR access request.</summary>
internal sealed class LeasePersonalData(MietHahnDbContext db) : IPersonalDataSource
{
    public string Section => "leases";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var leases = await db.Set<Lease>().AsNoTracking()
            .Include(v => v.Parties).Include(v => v.Components).Include(v => v.People).AsSplitQuery()
            .Where(v => v.Parties.Any(p => p.PersonId == personId))
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return null;
        }

        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        return leases.Select(v => new
        {
            v.Id,
            RentalUnit = rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Name,
            Property = rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Property,
            Role = v.Parties.Single(p => p.PersonId == personId).Role.ToString(),
            v.Term,
            v.FixedTermUntil,
            v.TerminationReceivedOn,
            TerminatedBy = v.TerminatedBy?.ToString(),
            BillingType = v.BillingType.ToString(),
            v.DueOnWorkingDay,
            PaymentMethod = v.PaymentMethod.ToString(),
            v.Deposit,
            v.DepositNote,
            v.Notes,
            Components = v.Components.OrderBy(b => b.Validity.From).Select(b => new { Kind = b.Kind.ToString(), b.Name, b.Validity, b.Amount }),
            OccupantCount = v.People.OrderBy(p => p.Validity.From).Select(p => new { p.Validity, p.Count }),
        }).ToList();
    }
}
