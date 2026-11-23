using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Deadlines;
using Elocaro.Application.Leases;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Common;
using Elocaro.Domain.Deadlines;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class ViewingService(ElocaroDbContext db, ILeaseService leases, ILandlordProfileService landlord, IOptions<ApplicationOptions> app, TimeProvider clock) : IViewings
{
    public async Task<IReadOnlyList<ViewingRow>> ListAsync(Guid? rentalUnitId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var query = db.Set<ViewingSlot>().AsNoTracking().Where(s => s.StartsAt >= now);
        if (rentalUnitId is { } unit)
        {
            query = query.Where(s => s.RentalUnitId == unit);
        }

        var slots = await query.OrderBy(s => s.StartsAt).ToListAsync(cancellationToken);
        var units = (await leases.RentalUnitsAsync(cancellationToken)).ToDictionary(u => u.Id);
        var ids = slots.Where(s => s.ProspectId is not null).Select(s => s.ProspectId!.Value).ToList();
        var names = await db.Set<Prospect>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return [.. slots.Select(s =>
        {
            units.TryGetValue(s.RentalUnitId, out var u);
            return new ViewingRow(s.Id, s.RentalUnitId, u?.Name ?? "", u?.Property ?? "", s.StartsAt, s.Minutes, s.ProspectId, s.ProspectId is { } p ? names.GetValueOrDefault(p) : null);
        })];
    }

    public Task<Result> AddAsync(ViewingInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var unit = input.RentalUnitId ?? throw new RuleViolationException("Bitte das Mietobjekt wählen.");
            if (!(await leases.RentalUnitsAsync(cancellationToken)).Any(u => u.Id == unit))
            {
                throw new NotFoundException();
            }

            var day = input.Day ?? throw new RuleViolationException("Bitte den Tag angeben.");
            var time = input.Time ?? throw new RuleViolationException("Bitte die Uhrzeit angeben.");
            DateTimeOffset start;
            try
            {
                start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time, DateTimeKind.Unspecified), GermanTime.Germany), TimeSpan.Zero);
            }
            catch (ArgumentException)
            {
                throw new RuleViolationException("Diese Uhrzeit gibt es am gewählten Tag nicht (Umstellung auf Sommerzeit).");
            }

            if (start <= clock.GetUtcNow())
            {
                throw new RuleViolationException("Der Termin muss in der Zukunft liegen.");
            }

            var slot = new ViewingSlot(unit, start, input.Minutes);
            db.Add(slot);
            await db.SaveChangesAsync(cancellationToken);
            return slot.Id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var slot = await db.Set<ViewingSlot>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (!slot.IsFree)
            {
                throw new RuleViolationException("Der Termin ist vergeben. Sagen Sie zuerst dem Interessenten ab.");
            }

            db.Remove(slot);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public async Task<ViewingLinkResult> CreateLinkAsync(Guid rentalUnitId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (!(await leases.RentalUnitsAsync(cancellationToken)).Any(u => u.Id == rentalUnitId))
        {
            return new ViewingLinkResult(Command.NotFound);
        }

        foreach (var open in await db.Set<ViewingLink>().Where(l => l.RentalUnitId == rentalUnitId && l.RevokedAt == null).ToListAsync(cancellationToken))
        {
            open.Revoke(now);
        }

        var token = ViewingLink.NewToken();
        var link = new ViewingLink(rentalUnitId, ViewingLink.Hash(token), now.AddDays(ViewingLink.Days));
        db.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return new ViewingLinkResult(Result.Ok(link.Id), app.Value.Link($"/besichtigung/{token}").ToString(), link.ExpiresAt);
    }

    public async Task<ViewingOffer?> OfferAsync(string token, CancellationToken cancellationToken = default)
    {
        if (await FindLinkAsync(token, cancellationToken) is not { } link)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var unit = (await leases.RentalUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Id == link.RentalUnitId);
        var profile = await landlord.GetAsync(cancellationToken);
        var controller = profile is null
            ? ""
            : string.Join(", ", new[] { profile.Name, profile.Street, $"{profile.PostalCode} {profile.City}", profile.Email, profile.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var slots = await db.Set<ViewingSlot>().AsNoTracking().Where(s => s.RentalUnitId == link.RentalUnitId && s.ProspectId == null && s.StartsAt > now).OrderBy(s => s.StartsAt).ToListAsync(cancellationToken);
        return new ViewingOffer(unit is null ? "" : $"{unit.Name}, {unit.Property}", controller, Prospect.RetentionMonthsWithoutDecision, [.. slots.Select(s => new OfferedSlot(s.Id, s.StartsAt, s.Minutes))]);
    }

    public Task<Result> BookAsync(string token, BookingInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var slotId = input.SlotId ?? throw new RuleViolationException("Bitte einen Termin wählen.");
            var link = await FindLinkAsync(token, cancellationToken) ?? throw new RuleViolationException("Dieser Link ist abgelaufen. Bitte wenden Sie sich an den Vermieter.");
            var now = clock.GetUtcNow();
            var slot = await db.Set<ViewingSlot>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == slotId && s.RentalUnitId == link.RentalUnitId, cancellationToken);
            if (slot is null || !slot.IsFree || slot.StartsAt <= now)
            {
                throw new RuleViolationException("Dieser Termin ist nicht mehr frei. Bitte wählen Sie einen anderen.");
            }

            var contact = input.Contact.Trim();
            var prospect = new Prospect(link.RentalUnitId, input.Name.Trim());
            if (contact.Contains('@', StringComparison.Ordinal))
            {
                prospect.Email = contact;
            }
            else
            {
                prospect.Phone = contact;
            }

            prospect.SetStatus(ProspectStatus.Viewing, GermanTime.Today(now), Prospect.RetentionMonthsAfterRejection);
            prospect.ViewingOn = GermanTime.Today(slot.StartsAt);
            db.Add(prospect);
            await db.SaveChangesAsync(cancellationToken);

            // Two people may pick the same date at the same time: the update only takes a still free date.
            var taken = await db.Set<ViewingSlot>().Where(s => s.Id == slotId && s.ProspectId == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProspectId, prospect.Id), cancellationToken);
            if (taken == 0)
            {
                await db.Set<Prospect>().Where(p => p.Id == prospect.Id).ExecuteDeleteAsync(cancellationToken);
                throw new RuleViolationException("Dieser Termin ist gerade vergeben worden. Bitte wählen Sie einen anderen.");
            }

            return slotId;
        });

    public async Task<string?> CalendarFileAsync(string token, Guid slotId, CancellationToken cancellationToken = default)
    {
        if (await FindLinkAsync(token, cancellationToken) is not { } link)
        {
            return null;
        }

        var slot = await db.Set<ViewingSlot>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == slotId && s.RentalUnitId == link.RentalUnitId, cancellationToken);
        var unit = (await leases.RentalUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Id == link.RentalUnitId);
        return slot is null || unit is null ? null : ICalendarWriter.WriteEvent($"viewing:{slot.Id}", slot.StartsAt, slot.EndsAt, $"Besichtigung {unit.Name}", unit.Property, clock.GetUtcNow());
    }

    /// <summary>The link behind a token: unknown, expired and withdrawn links all look the same.</summary>
    private async Task<ViewingLink?> FindLinkAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = ViewingLink.Hash(token);
        var link = await db.Set<ViewingLink>().AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        return link is not null && link.IsOpen(clock.GetUtcNow()) ? link : null;
    }
}

/// <summary>Booked viewings of the coming weeks in the deadline calendar (only with the module on).</summary>
internal sealed class ViewingDeadlineSource(IViewings viewings, IModuleState modules) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineEntry>> DeadlinesAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        if (!modules.IsEnabled(AppModule.Prospects))
        {
            return [];
        }

        return [.. (await viewings.ListAsync(null, cancellationToken))
            .Where(v => v.ProspectId is not null)
            .Select(v => new DeadlineEntry(
                $"viewing:{v.Id}",
                DeadlineOrigin.Derived,
                "Vermarktung",
                $"Besichtigung {v.RentalUnit}, {v.Property}: {v.ProspectName}",
                GermanTime.Today(v.StartsAt),
                "/verwaltung/besichtigungen",
                AccessRight.MasterData,
                $"Um {TimeZoneInfo.ConvertTime(v.StartsAt, GermanTime.Germany):HH:mm} Uhr, {v.Minutes} Minuten.",
                LeadDays: 1))
            .Where(e => e.Due <= until)];
    }
}
