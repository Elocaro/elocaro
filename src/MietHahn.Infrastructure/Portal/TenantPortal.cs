using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Portal;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Portal;

internal sealed class TenantPortal(MietHahnDbContext db, TimeProvider clock) : ITenantPortal
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<PortalLeaseRow>> LeasesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var leases = await OwnLeases(await PersonIdsAsync(user, cancellationToken)).AsNoTracking().Include(v => v.Components).ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return [];
        }

        var units = await RentalUnit.GetAsync(db, cancellationToken);
        var today = Today;
        return leases
            .OrderBy(v => v.StatusAt(today) == LeaseStatus.Active ? 0 : 1)
            .ThenByDescending(v => v.Start)
            .Select(v => new PortalLeaseRow(v.Id, units.GetValueOrDefault(v.RentalUnitId)?.Name ?? "", units.GetValueOrDefault(v.RentalUnitId)?.Property ?? "", v.Start, v.End, v.StatusAt(today), v.MonthlyRentOn(today)))
            .ToList();
    }

    public async Task<PortalLease?> GetLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default)
    {
        var lease = await OwnLeases(await PersonIdsAsync(user, cancellationToken)).AsNoTracking()
            .Include(v => v.Components).Include(v => v.Parties)
            .SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var today = Today;
        var unit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId);
        var personIds = lease.Tenant.Select(p => p.PersonId).ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        return new PortalLease(
            lease.Id,
            unit?.Name ?? "",
            unit?.Property ?? "",
            lease.Start,
            lease.End,
            lease.FixedTermUntil,
            lease.StatusAt(today),
            lease.Tenant.Select(p => names.GetValueOrDefault(p.PersonId, "?")).ToList(),
            lease.ComponentsAt(today).OrderBy(c => c.Kind).Select(c => new PortalRentComponent(c.Kind, c.Name, c.Amount)).ToList(),
            lease.MonthlyRentOn(today),
            lease.BillingType,
            lease.DueOnWorkingDay,
            lease.PaymentMethod,
            lease.Deposit);
    }

    public async Task<PortalContacts?> ContactsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var personIds = await PersonIdsAsync(user, cancellationToken);
        if (personIds.Count == 0)
        {
            return null;
        }

        var person = await db.Set<Person>().AsNoTracking().OrderBy(p => p.CreatedAt).FirstAsync(p => personIds.Contains(p.Id), cancellationToken);
        var pending = await db.Set<ContactChangeRequest>().AsNoTracking()
            .Where(a => a.PersonId == person.Id && a.Status == ContactChangeStatus.Open)
            .OrderByDescending(a => a.RequestedAt).FirstOrDefaultAsync(cancellationToken);

        var responsible = new List<PortalContact>();
        var firstUnit = OwnLeases(personIds).Select(v => v.UnitId).FirstOrDefault();
        var landlord = firstUnit == Guid.Empty
            ? await Admin.LandlordProfiles.ForPropertyAsync(db, null, cancellationToken)
            : await Admin.LandlordProfiles.ForUnitAsync(db, firstUnit, cancellationToken);
        if (landlord is not null)
        {
            responsible.Add(new PortalContact("Vermieter", landlord.Name, landlord.Phone, landlord.Email));
        }

        var units = OwnLeases(personIds).Select(v => v.UnitId);
        var properties = db.Set<Unit>().Where(e => units.Contains(e.Id)).Select(e => e.PropertyId).Distinct();
        var caretakerUsers = db.Set<PropertyAssignment>().Where(z => properties.Contains(z.PropertyId)).Select(z => z.UserId).Distinct();
        var caretakers = await db.Set<Person>().AsNoTracking()
            .Where(p => p.UserId != null && caretakerUsers.Contains(p.UserId.Value) && p.ContactType == ContactType.Caretaker)
            .OrderBy(p => p.LastName).ToListAsync(cancellationToken);
        responsible.AddRange(caretakers.Select(p => new PortalContact("Hausmeister", p.DisplayName, p.Phone ?? p.Mobile, p.Email)));

        return new PortalContacts(
            new ContactChangeInput { Email = person.Email, Phone = person.Phone, Mobile = person.Mobile },
            pending is null ? null : new ContactChangeInput { Email = pending.Email, Phone = pending.Phone, Mobile = pending.Mobile },
            responsible)
        {
            EmergencyInfo = landlord?.EmergencyInfo,
        };
    }

    public Task<Result> RequestContactChangeAsync(ClaimsPrincipal user, ContactChangeInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var personIds = await PersonIdsAsync(user, cancellationToken);
            var person = personIds.Count == 0
                ? throw new RuleViolationException("Ihr Konto ist keiner Person zugeordnet.")
                : await db.Set<Person>().OrderBy(p => p.CreatedAt).FirstAsync(p => personIds.Contains(p.Id), cancellationToken);

            // A new request replaces the open one.
            var open = await db.Set<ContactChangeRequest>().Where(a => a.PersonId == person.Id && a.Status == ContactChangeStatus.Open).ToListAsync(cancellationToken);
            foreach (var old in open)
            {
                old.Reject("Durch einen neuen Antrag ersetzt.", clock.GetUtcNow());
            }

            var request = new ContactChangeRequest(person.Id, input.Email, input.Phone, input.Mobile, clock.GetUtcNow());
            db.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            return request.Id;
        });

    public async Task MarkReadAsync(ClaimsPrincipal user, Guid fileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Tenant) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        // Only documents released for this very account count, whatever file id the caller passes.
        var visible = await DocumentShares.VisibleAsync(db, user, Today, cancellationToken);
        var documents = await visible.Where(d => d.FileId == fileId).Select(d => d.Id).ToListAsync(cancellationToken);
        var known = await db.Set<DocumentRead>().Where(l => l.UserId == userId && documents.Contains(l.DocumentId)).Select(l => l.DocumentId).ToListAsync(cancellationToken);
        var added = false;
        foreach (var id in documents.Except(known))
        {
            db.Add(new DocumentRead(id, userId, clock.GetUtcNow()));
            added = true;
        }

        if (added)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Parallel request has already stored it (unique index): fine.
            }
        }
    }

    public async Task<IReadOnlySet<Guid>> ReadDocumentsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return new HashSet<Guid>();
        }

        return (await db.Set<DocumentRead>().AsNoTracking().Where(l => l.UserId == userId).Select(l => l.DocumentId).ToListAsync(cancellationToken)).ToHashSet();
    }

    private async Task<List<Guid>> PersonIdsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Tenant) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return [];
        }

        return await db.Set<Person>().AsNoTracking().Where(p => p.UserId == userId && p.AnonymizedAt == null).Select(p => p.Id).ToListAsync(cancellationToken);
    }

    private IQueryable<Lease> OwnLeases(List<Guid> personIds) =>
        db.Set<Lease>().Where(v => v.Parties.Any(p => personIds.Contains(p.PersonId) && p.Role == PartyRole.Tenant));
}
