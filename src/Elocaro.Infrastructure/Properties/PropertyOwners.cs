using Elocaro.Application.Common;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

internal sealed class PropertyOwners(ElocaroDbContext db, TimeProvider clock) : IPropertyOwners
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<PropertyOwnership> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var owners = await db.Set<PropertyOwner>().AsNoTracking().Where(o => o.PropertyId == propertyId).ToListAsync(cancellationToken);
        var ids = owners.Select(o => o.PersonId).ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        var today = Today;
        var rows = owners
            .OrderByDescending(o => o.IsValidOn(today)).ThenBy(o => names.GetValueOrDefault(o.PersonId, ""), StringComparer.CurrentCulture).ThenBy(o => o.ValidFrom)
            .Select(o => new OwnerRow(o.Id, o.PersonId, names.GetValueOrDefault(o.PersonId, "?"), o.SharePercent, o.ValidFrom, o.ValidTo, o.IsValidOn(today))).ToList();

        var hints = new List<string>();
        var current = owners.Where(o => o.IsValidOn(today)).Sum(o => o.SharePercent);
        if (owners.Count > 0 && current != 100m)
        {
            hints.Add(current == 0m
                ? "Heute ist kein Eigentümer gültig."
                : $"Die heute gültigen Anteile ergeben zusammen {current:0.##} % statt 100 %.");
        }

        return new PropertyOwnership(rows, hints);
    }

    public Task<Result> AddAsync(Guid propertyId, OwnerInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var personId = input.PersonId ?? throw new RuleViolationException("Bitte die Person wählen.");
            if (!await db.Set<Person>().AnyAsync(p => p.Id == personId && p.AnonymizedAt == null, cancellationToken))
            {
                throw new RuleViolationException("Die Person wurde nicht gefunden.");
            }

            var owner = new PropertyOwner(propertyId, personId, input.SharePercent ?? throw new RuleViolationException("Bitte den Anteil angeben."), input.ValidFrom, input.ValidTo);
            var existing = await db.Set<PropertyOwner>().Where(o => o.PropertyId == propertyId).ToListAsync(cancellationToken);
            if (existing.Any(o => o.Overlaps(owner)))
            {
                throw new RuleViolationException("Diese Person ist für den Zeitraum schon als Eigentümer eingetragen. Bitte den bestehenden Eintrag beenden.");
            }

            if (PropertyOwner.ExceedsFullShare(existing, owner))
            {
                throw new RuleViolationException("Die Anteile ergeben im Zeitraum mehr als 100 %.");
            }

            db.Add(owner);
            await db.SaveChangesAsync(cancellationToken);
            return owner.Id;
        });

    public Task<Result> UpdateAsync(Guid propertyId, Guid ownerId, OwnerInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var existing = await db.Set<PropertyOwner>().Where(o => o.PropertyId == propertyId).ToListAsync(cancellationToken);
            var owner = existing.SingleOrDefault(o => o.Id == ownerId) ?? throw new NotFoundException();
            var personId = input.PersonId ?? throw new RuleViolationException("Bitte die Person wählen.");
            if (!await db.Set<Person>().AnyAsync(p => p.Id == personId && p.AnonymizedAt == null, cancellationToken))
            {
                throw new RuleViolationException("Die Person wurde nicht gefunden.");
            }

            var candidate = new PropertyOwner(propertyId, personId, input.SharePercent ?? throw new RuleViolationException("Bitte den Anteil angeben."), input.ValidFrom, input.ValidTo);
            var others = existing.Where(o => o.Id != ownerId).ToList();
            if (others.Any(o => o.Overlaps(candidate)))
            {
                throw new RuleViolationException("Diese Person ist für den Zeitraum schon als Eigentümer eingetragen.");
            }

            if (PropertyOwner.ExceedsFullShare(others, candidate))
            {
                throw new RuleViolationException("Die Anteile ergeben im Zeitraum mehr als 100 %.");
            }

            owner.Change(personId, candidate.SharePercent, input.ValidFrom, input.ValidTo);
            await db.SaveChangesAsync(cancellationToken);
            return ownerId;
        });

    public Task<Result> EndAsync(Guid propertyId, Guid ownerId, DateOnly day, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var owner = await db.Set<PropertyOwner>().SingleOrDefaultAsync(o => o.Id == ownerId && o.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            owner.End(day);
            await db.SaveChangesAsync(cancellationToken);
            return ownerId;
        });

    public Task<Result> RemoveAsync(Guid propertyId, Guid ownerId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var owner = await db.Set<PropertyOwner>().SingleOrDefaultAsync(o => o.Id == ownerId && o.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(owner);
            await db.SaveChangesAsync(cancellationToken);
            return ownerId;
        });

    public async Task<IReadOnlyList<OwnedProperty>> OwnedByAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var owned = (await db.Set<PropertyOwner>().AsNoTracking().Where(o => o.PersonId == personId).ToListAsync(cancellationToken)).Where(o => o.IsValidOn(today)).ToList();
        var ids = owned.Select(o => o.PropertyId).ToList();
        var names = await db.Set<Property>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return owned.Select(o => new OwnedProperty(o.PropertyId, names.GetValueOrDefault(o.PropertyId, "?"), o.SharePercent)).OrderBy(o => o.Name, StringComparer.CurrentCulture).ToList();
    }
}

/// <summary>Owners cannot be deleted while they own a property.</summary>
internal sealed class PropertyOwnerReferences(ElocaroDbContext db, TimeProvider clock) : IPersonReference
{
    public async Task<IReadOnlyList<PersonRelation>> ReferencesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        var today = GermanTime.Today(clock.GetUtcNow());
        var owners = await db.Set<PropertyOwner>().AsNoTracking().Where(o => personIds.Contains(o.PersonId)).ToListAsync(cancellationToken);
        var ids = owners.Select(o => o.PropertyId).Distinct().ToList();
        var names = await db.Set<Property>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return owners.Select(o => new PersonRelation(o.PersonId, "Eigentümer", $"{names.GetValueOrDefault(o.PropertyId, "?")} ({o.SharePercent:0.##} %)", o.IsValidOn(today), o.ValidTo, $"/verwaltung/objekte/{o.PropertyId}")).ToList();
    }
}
