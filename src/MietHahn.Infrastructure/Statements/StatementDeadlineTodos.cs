using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Statements;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Statements;

/// <summary>
/// Dashboard: reminder to create the operating cost statement from October of the following year, warnings 6, 4 and 2 weeks
/// before the deadline under § 556 para. 3 BGB (31 December) and a note once it has passed.
/// </summary>
[Module(AppModule.OperatingCosts)]
internal sealed class StatementDeadlineTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var properties = await db.Set<Property>().AsNoTracking().OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var released = (await db.Set<BillingStatement>().AsNoTracking()
            .Select(s => new { s.PropertyId, s.BillingPeriod.From, s.BillingPeriod.To }).ToListAsync(cancellationToken))
            .Select(s => (s.PropertyId, Period: new Period(s.From, s.To))).ToHashSet();

        var todos = new List<TodoItem>();
        // The previous year is due; the year before is only shown while its deadline has just passed without a statement.
        foreach (var year in new[] { today.Year - 1, today.Year - 2 })
        {
            var period = Period.CalendarYear(year);
            if (StatementDeadline.StateOn(period, today) is not { } state)
            {
                continue;
            }

            var billed = await db.Set<Lease>().AsNoTracking()
                .Where(v => v.BillingType == BillingType.Prepayment && v.Term.From <= period.To && (v.Term.To == null || v.Term.To >= period.From))
                .Join(db.Set<Unit>(), v => v.UnitId, u => u.Id, (v, u) => u.PropertyId)
                .Distinct().ToListAsync(cancellationToken);
            foreach (var property in properties.Where(p => billed.Contains(p.Id) && !released.Contains((p.Id, period))))
            {
                var title = state.Level == DeadlineLevel.Expired
                    ? $"Frist für die Abrechnung {year} verstrichen: {property.Name}"
                    : $"Abrechnung {year} erstellen: {property.Name}";
                var details = state.Level == DeadlineLevel.Expired
                    ? $"Die Frist nach § 556 Abs. 3 BGB endete am {state.Deadline:dd.MM.yyyy}; Nachforderungen sind ausgeschlossen, Guthaben müssen erstattet werden."
                    : $"Noch {state.Days} Tage bis zum {state.Deadline:dd.MM.yyyy} (§ 556 Abs. 3 BGB).";
                todos.Add(new TodoItem(
                    "Abrechnung",
                    title,
                    $"/verwaltung/objekte/{property.Id}/abrechnung?jahr={year}",
                    state.Level >= DeadlineLevel.Warning ? Urgency.Urgent : state.Level == DeadlineLevel.Notice ? Urgency.Soon : Urgency.Info,
                    details,
                    state.Deadline));
            }
        }

        return todos;
    }
}
