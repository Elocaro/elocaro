using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Tickets;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Tickets;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Notices;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tickets;

internal sealed class CareRoutines(MietHahnDbContext db, TimeProvider clock, IPdfGenerator pdf) : ICareRoutines
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<RoutineRow>> ListAsync(ClaimsPrincipal user, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var properties = await VisiblePropertiesAsync(user, cancellationToken);
        var routines = await db.Set<CareRoutine>().AsNoTracking()
            .Where(r => (includeInactive || r.Active) && (properties == null || properties.Contains(r.PropertyId))).ToListAsync(cancellationToken);
        if (routines.Count == 0)
        {
            return [];
        }

        var ids = routines.Select(r => r.Id).ToList();
        var checks = (await db.Set<CareCheck>().AsNoTracking().Where(c => ids.Contains(c.RoutineId)).ToListAsync(cancellationToken)).ToLookup(c => c.RoutineId);
        var names = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var today = Today;
        return routines.Select(r =>
        {
            var last = checks[r.Id].OrderByDescending(c => c.DoneOn).ThenByDescending(c => c.CreatedAt).FirstOrDefault();
            return new RoutineRow(r.Id, r.PropertyId, names.GetValueOrDefault(r.PropertyId, ""), r.Title, r.Description, r.Interval, r.Active, last?.DoneOn, last?.DoneBy,
                last is null ? null : r.NextDue(last.DoneOn), r.Active && r.IsDue(last?.DoneOn, today), checks[r.Id].Count(c => c.DoneOn.Year == today.Year));
        }).OrderByDescending(r => r.Due).ThenBy(r => r.Property).ThenBy(r => r.Title).ToList();
    }

    public Task<Result> CreateAsync(ClaimsPrincipal user, RoutineInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!TenantScope.IsAdmin(user))
            {
                throw new RuleViolationException("Dafür fehlt die Berechtigung.");
            }

            var propertyId = input.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new RuleViolationException("Das Objekt wurde nicht gefunden.");
            }

            var routine = new CareRoutine(propertyId, input.Title, input.Interval, input.Description);
            db.Add(routine);
            await db.SaveChangesAsync(cancellationToken);
            return routine.Id;
        });

    public Task<Result> DeactivateAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            if (!TenantScope.IsAdmin(user))
            {
                throw new RuleViolationException("Dafür fehlt die Berechtigung.");
            }

            var routine = await db.Set<CareRoutine>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            routine.Deactivate();
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> CheckAsync(ClaimsPrincipal user, Guid id, DateOnly? doneOn, string? note, Guid? fileId = null, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var properties = await VisiblePropertiesAsync(user, cancellationToken);
            var routine = await db.Set<CareRoutine>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && (properties == null || properties.Contains(r.PropertyId)), cancellationToken)
                ?? throw new NotFoundException();
            if (!routine.Active)
            {
                throw new RuleViolationException("Diese Aufgabe ist nicht mehr aktiv.");
            }

            var today = Today;
            var day = doneOn ?? today;
            if (day > today)
            {
                throw new RuleViolationException("Das Datum darf nicht in der Zukunft liegen.");
            }

            var userId = TenantScope.UserId(user);
            var name = userId is { } uid ? await db.Set<ApplicationUser>().Where(u => u.Id == uid).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "Unbekannt" : "Unbekannt";
            var check = new CareCheck(id, day, userId, name, note, fileId);
            db.Add(check);
            await db.SaveChangesAsync(cancellationToken);
            return check.Id;
        });

    public async Task<IReadOnlyList<CheckRow>> ChecksAsync(ClaimsPrincipal user, Guid id, int? year, CancellationToken cancellationToken = default)
    {
        var properties = await VisiblePropertiesAsync(user, cancellationToken);
        if (!await db.Set<CareRoutine>().AnyAsync(r => r.Id == id && (properties == null || properties.Contains(r.PropertyId)), cancellationToken))
        {
            return [];
        }

        return (await db.Set<CareCheck>().AsNoTracking().Where(c => c.RoutineId == id && (year == null || c.DoneOn.Year == year)).OrderByDescending(c => c.DoneOn).ThenByDescending(c => c.CreatedAt).ToListAsync(cancellationToken))
            .Select(c => new CheckRow(c.Id, c.DoneOn, c.DoneBy, c.Note, c.FileId)).ToList();
    }

    public async Task<byte[]> ProofPdfAsync(ClaimsPrincipal user, int year, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            throw new RuleViolationException("Dafür fehlt die Berechtigung.");
        }

        var routines = await ListAsync(user, true, cancellationToken);
        var groups = new List<object>();
        foreach (var property in routines.GroupBy(r => r.Property).OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            var items = new List<object>();
            foreach (var routine in property.OrderBy(r => r.Title, StringComparer.CurrentCulture))
            {
                var checks = await ChecksAsync(user, routine.Id, year, cancellationToken);
                items.Add(new
                {
                    routine.Title,
                    Interval = IntervalText(routine.Interval),
                    Count = checks.Count,
                    Checks = checks.OrderBy(c => c.DoneOn).Select(c => new { c.DoneOn, c.DoneBy, c.Note, HasPhoto = c.FileId != null }).ToList(),
                });
            }

            groups.Add(new { Property = property.Key, Routines = items });
        }

        return await pdf.GenerateAsync(new PdfRequest("care-proof", $"Nachweis Hausmeisteraufgaben {year}", new { Year = year, Properties = groups }, null, null, Today), cancellationToken);
    }

    private static string IntervalText(CareInterval interval) => interval switch
    {
        CareInterval.Daily => "täglich",
        CareInterval.Weekly => "wöchentlich",
        CareInterval.Monthly => "monatlich",
        CareInterval.Quarterly => "vierteljährlich",
        _ => "jährlich",
    };

    /// <summary><c>null</c> = all properties (administration); otherwise the properties assigned to the caretaker (possibly none).</summary>
    private async Task<List<Guid>?> VisiblePropertiesAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (TenantScope.IsAdmin(user))
        {
            return null;
        }

        if (!user.IsInRole(Roles.Caretaker) || TenantScope.UserId(user) is not { } userId)
        {
            return [];
        }

        return await db.Set<PropertyAssignment>().AsNoTracking().Where(z => z.UserId == userId).Select(z => z.PropertyId).ToListAsync(cancellationToken);
    }
}

/// <summary>Dashboard: caretaker duties whose due day has passed without a tick.</summary>
[Module(AppModule.Tickets)]
internal sealed class CareRoutineTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!TenantScope.IsAdmin(user))
        {
            return [];
        }

        var routines = await db.Set<CareRoutine>().AsNoTracking().Where(r => r.Active).ToListAsync(cancellationToken);
        if (routines.Count == 0)
        {
            return [];
        }

        var ids = routines.Select(r => r.Id).ToList();
        var lastDone = (await db.Set<CareCheck>().AsNoTracking().Where(c => ids.Contains(c.RoutineId)).GroupBy(c => c.RoutineId)
            .Select(g => new { g.Key, Last = g.Max(c => c.DoneOn) }).ToListAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.Last);
        var names = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var today = GermanTime.Today(clock.GetUtcNow());
        return routines
            .Select(r =>
            {
                DateOnly? last = lastDone.TryGetValue(r.Id, out var day) ? day : null;
                var baseline = last ?? GermanTime.Today(r.CreatedAt);
                return (Routine: r, Last: last, Overdue: r.IsOverdue(last, GermanTime.Today(r.CreatedAt), today), Due: r.NextDue(baseline));
            })
            .Where(x => x.Overdue)
            .OrderBy(x => x.Due)
            .Select(x => new TodoItem(
                "Tickets",
                $"Hausmeisteraufgabe überfällig: {x.Routine.Title} ({names.GetValueOrDefault(x.Routine.PropertyId, "")})",
                "/verwaltung/tickets/routinen",
                today.DayNumber - x.Due.DayNumber >= 7 ? Urgency.Urgent : Urgency.Soon,
                x.Last is { } done ? $"Zuletzt abgehakt am {done:dd.MM.yyyy}." : "Bisher nicht abgehakt.",
                x.Due))
            .ToList();
    }
}
