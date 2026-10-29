using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

internal sealed class PropertyLandlords(ElocaroDbContext db, TimeProvider clock) : IPropertyLandlords
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<LandlordHistory> GetAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var profiles = await db.Set<LandlordProfile>().AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var names = profiles.ToDictionary(p => p.Id, p => p.Name);
        var chosen = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => o.LandlordProfileId).SingleOrDefaultAsync(cancellationToken);
        var baseProfile = chosen is { } id && names.TryGetValue(id, out var chosenName) ? chosenName : profiles.FirstOrDefault()?.Name;
        var entries = await db.Set<PropertyLandlord>().AsNoTracking().Where(a => a.PropertyId == propertyId).OrderBy(a => a.ValidFrom).ToListAsync(cancellationToken);
        var today = Today;
        var currentId = entries.LastOrDefault(a => a.ValidFrom <= today)?.Id;
        return new LandlordHistory(
            baseProfile,
            [.. entries.Select(a => new LandlordEntry(a.Id, a.LandlordProfileId, names.GetValueOrDefault(a.LandlordProfileId, "?"), a.ValidFrom, a.Id == currentId))],
            [.. profiles.Select(p => (p.Id, p.Name))]);
    }

    public Task<Result> AddAsync(Guid propertyId, LandlordChangeInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var (profile, from) = await CheckedAsync(propertyId, null, input, cancellationToken);
            var entry = new PropertyLandlord(propertyId, profile, from);
            db.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entry.Id;
        });

    public Task<Result> UpdateAsync(Guid propertyId, Guid entryId, LandlordChangeInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var entry = await db.Set<PropertyLandlord>().SingleOrDefaultAsync(a => a.Id == entryId && a.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            var (profile, from) = await CheckedAsync(propertyId, entryId, input, cancellationToken);
            entry.Change(profile, from);
            await db.SaveChangesAsync(cancellationToken);
            return entryId;
        });

    public Task<Result> RemoveAsync(Guid propertyId, Guid entryId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var entry = await db.Set<PropertyLandlord>().SingleOrDefaultAsync(a => a.Id == entryId && a.PropertyId == propertyId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entryId;
        });

    private async Task<(Guid Profile, DateOnly From)> CheckedAsync(Guid propertyId, Guid? exceptId, LandlordChangeInput input, CancellationToken cancellationToken)
    {
        var profile = input.ProfileId ?? throw new RuleViolationException("Bitte das Vermieterprofil wählen.");
        var from = input.From ?? throw new RuleViolationException("Bitte angeben, ab wann das Profil gilt.");
        if (!await db.Set<LandlordProfile>().AnyAsync(p => p.Id == profile, cancellationToken))
        {
            throw new RuleViolationException("Das Vermieterprofil wurde nicht gefunden.");
        }

        if (await db.Set<PropertyLandlord>().AnyAsync(a => a.PropertyId == propertyId && a.ValidFrom == from && a.Id != exceptId, cancellationToken))
        {
            throw new RuleViolationException("Für diesen Tag gibt es schon einen Eintrag.");
        }

        return (profile, from);
    }
}
