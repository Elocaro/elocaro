using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Common;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class ProspectService(ElocaroDbContext db, ILeaseService leases, IPersonService people, TimeProvider clock) : IProspects
{
    public async Task<IReadOnlyList<ProspectRow>> ListAsync(Guid? rentalUnitId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Set<Prospect>().AsNoTracking();
        if (rentalUnitId is { } unit)
        {
            query = query.Where(p => p.RentalUnitId == unit);
        }

        var prospects = await query.ToListAsync(cancellationToken);
        var units = (await leases.RentalUnitsAsync(cancellationToken)).ToDictionary(u => u.Id);
        return [.. prospects
            .Select(p =>
            {
                units.TryGetValue(p.RentalUnitId, out var unit);
                return new ProspectRow(p.Id, p.RentalUnitId, unit?.Name ?? "", unit?.Property ?? "", p.Name, p.Email, p.Phone, p.Status, p.ViewingOn, p.DeletionDay, p.PersonId);
            })
            .OrderBy(r => r.Status is ProspectStatus.Accepted or ProspectStatus.Declined)
            .ThenBy(r => r.Property, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.RentalUnit, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<ProspectInput?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Set<Prospect>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } p
            ? new ProspectInput { RentalUnitId = p.RentalUnitId, Name = p.Name, Email = p.Email, Phone = p.Phone, ViewingOn = p.ViewingOn, Notes = p.Notes }
            : null;

    public Task<Result> CreateAsync(ProspectInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var prospect = new Prospect(input.RentalUnitId ?? throw new RuleViolationException("Bitte das Mietobjekt wählen."), input.Name.Trim());
            Apply(prospect, input);
            db.Add(prospect);
            await db.SaveChangesAsync(cancellationToken);
            return prospect.Id;
        });

    public Task<Result> UpdateAsync(Guid id, ProspectInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var prospect = await db.Set<Prospect>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            prospect.Rename(input.Name.Trim());
            Apply(prospect, input);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> SetStatusAsync(Guid id, ProspectStatus status, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var prospect = await db.Set<Prospect>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            prospect.SetStatus(status, GermanTime.Today(clock.GetUtcNow()), Prospect.RetentionMonthsAfterRejection);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<Result> AdoptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var prospect = await db.Set<Prospect>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prospect is null)
        {
            return Command.NotFound;
        }

        if (prospect.PersonId is { } existing)
        {
            return Result.Ok(existing);
        }

        // The last word is the family name; "Muster GmbH" or a single word stays the family name as a whole.
        var name = prospect.Name.Trim();
        var split = name.LastIndexOf(' ');
        var created = await people.CreateAsync(
            new PersonInput { FirstName = split > 0 ? name[..split] : null, LastName = split > 0 ? name[(split + 1)..] : name, Email = prospect.Email, Phone = prospect.Phone },
            cancellationToken: cancellationToken);
        if (created is not { Succeeded: true, Id: { } personId })
        {
            return created;
        }

        return await Command.ExecuteAsync(async () =>
        {
            var today = GermanTime.Today(clock.GetUtcNow());
            prospect.AdoptAs(personId, today);

            // The unit goes to this one: the data of the other open prospects is no longer needed (DSK guidance 2.0, section D).
            foreach (var other in await db.Set<Prospect>().Where(o => o.RentalUnitId == prospect.RentalUnitId && o.Id != prospect.Id && o.Status != ProspectStatus.Accepted && o.Status != ProspectStatus.Declined).ToListAsync(cancellationToken))
            {
                other.ScheduleDeletion(today);
            }

            await db.SaveChangesAsync(cancellationToken);
            return personId;
        });
    }

    private static void Apply(Prospect prospect, ProspectInput input)
    {
        prospect.Email = TrimToNull(input.Email);
        prospect.Phone = TrimToNull(input.Phone);
        prospect.ViewingOn = input.ViewingOn;
        prospect.Notes = TrimToNull(input.Notes);
    }

    private static string? TrimToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
