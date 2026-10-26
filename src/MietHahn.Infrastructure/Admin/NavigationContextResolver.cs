using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Admin;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Statements;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

/// <summary>
/// Runs in the layout, next to the page: a context of its own per call, because the page uses the scoped one at the same time
/// and a DbContext does not allow concurrent operations.
/// </summary>
internal sealed class NavigationContextResolver(IServiceScopeFactory scopes) : INavigationContext
{
    private const string Root = "/verwaltung";

    private static readonly TrailStep Properties = new("Objekte", $"{Root}/objekte");
    private static readonly TrailStep Leases = new("Verträge", $"{Root}/vertraege");
    private static readonly TrailStep People = new("Personen", $"{Root}/personen");

    public async Task<PageContext?> ResolveAsync(string path, CancellationToken cancellationToken = default)
    {
        var parts = path.Trim('/').Split('/');
        if (parts is ["verwaltung", "finanzen", "darlehen", { } loan, ..] && Guid.TryParse(loan, out var loanId))
        {
            await using var loanScope = scopes.CreateAsyncScope();
            return await LoanAsync(loanScope.ServiceProvider.GetRequiredService<MietHahnDbContext>(), loanId, cancellationToken);
        }

        if (parts.Length < 3 || !parts[0].Equals("verwaltung", StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(parts[2], out var id))
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        return parts[1].ToLowerInvariant() switch
        {
            "objekte" => await PropertyAsync(db, id, cancellationToken),
            "einheiten" => await UnitAsync(db, id, null, cancellationToken),
            "zimmer" => await RoomAsync(db, id, cancellationToken),
            "zaehler" => await MeterAsync(db, id, cancellationToken),
            "abrechnungen" => await StatementAsync(db, id, cancellationToken),
            "vertraege" => await LeaseAsync(db, id, cancellationToken),
            "personen" => await PersonAsync(db, id, cancellationToken),
            _ => null,
        };
    }

    private static async Task<PageContext?> PropertyAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var name = await db.Set<Property>().AsNoTracking().Where(o => o.Id == id).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        return name is null ? null : new([Properties, PropertyStep(id, name)], id);
    }

    private static async Task<PageContext?> UnitAsync(MietHahnDbContext db, Guid id, TrailStep? room, CancellationToken cancellationToken)
    {
        var unit = await db.Set<Unit>().AsNoTracking().Where(e => e.Id == id).Select(e => new { e.Name, e.PropertyId }).SingleOrDefaultAsync(cancellationToken);
        if (unit is null || await PropertyAsync(db, unit.PropertyId, cancellationToken) is not { } property)
        {
            return null;
        }

        List<TrailStep> trail = [.. property.Trail, new(unit.Name, $"{Root}/einheiten/{id}")];
        if (room is not null)
        {
            trail.Add(room);
        }

        return new(trail, property.PropertyId);
    }

    private static async Task<PageContext?> RoomAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var room = await db.Set<Room>().AsNoTracking().Where(z => z.Id == id).Select(z => new { z.Name, z.UnitId }).SingleOrDefaultAsync(cancellationToken);
        return room is null ? null : await UnitAsync(db, room.UnitId, new(room.Name, $"{Root}/zimmer/{id}"), cancellationToken);
    }

    private static async Task<PageContext?> MeterAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var meter = await db.Set<Meter>().AsNoTracking().Where(z => z.Id == id).Select(z => new { z.Name, z.PropertyId }).SingleOrDefaultAsync(cancellationToken);
        if (meter is null || await PropertyAsync(db, meter.PropertyId, cancellationToken) is not { } property)
        {
            return null;
        }

        return new([.. property.Trail, new("Zähler", $"{Root}/objekte/{meter.PropertyId}/zaehler"), new(meter.Name, $"{Root}/zaehler/{id}")], meter.PropertyId);
    }

    private static async Task<PageContext?> StatementAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var statement = await db.Set<BillingStatement>().AsNoTracking().Where(a => a.Id == id).Select(a => new { a.PropertyId, a.BillingPeriod, a.Revision }).SingleOrDefaultAsync(cancellationToken);
        if (statement is null || await PropertyAsync(db, statement.PropertyId, cancellationToken) is not { } property)
        {
            return null;
        }

        var period = statement.BillingPeriod.From.Year == statement.BillingPeriod.To?.Year
            ? $"Abrechnung {statement.BillingPeriod.From.Year}"
            : $"Abrechnung {statement.BillingPeriod.From:dd.MM.yyyy}";
        return new([.. property.Trail, new("Abrechnungen", $"{Root}/objekte/{statement.PropertyId}/abrechnungen"), new(period, $"{Root}/abrechnungen/{id}")], statement.PropertyId);
    }

    private static async Task<PageContext?> LeaseAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Where(v => v.Id == id)
            .Select(v => new { v.UnitId, v.RoomId, Tenants = v.Parties.Where(p => p.Role == PartyRole.Tenant).OrderBy(p => p.Order).Select(p => p.PersonId).ToList() })
            .SingleOrDefaultAsync(cancellationToken);
        var unit = lease is null ? null : await db.Set<Unit>().AsNoTracking().Where(e => e.Id == lease.UnitId).Select(e => new { e.Name, e.PropertyId }).SingleOrDefaultAsync(cancellationToken);
        if (lease is null || unit is null)
        {
            return null;
        }

        var propertyName = await db.Set<Property>().AsNoTracking().Where(o => o.Id == unit.PropertyId).Select(o => o.Name).SingleAsync(cancellationToken);
        var tenants = await db.Set<Person>().AsNoTracking().Where(p => lease.Tenants.Contains(p.Id)).ToListAsync(cancellationToken);
        var tenant = string.Join(", ", lease.Tenants.Select(t => tenants.SingleOrDefault(p => p.Id == t)?.DisplayName).Where(n => n is not null));
        var label = tenant.Length > 0 ? $"{unit.Name} · {tenant}" : unit.Name;
        return new([Leases, PropertyStep(unit.PropertyId, propertyName), new(label, $"{Root}/vertraege/{id}")], null);
    }

    /// <summary>A loan belongs to a property but lives under Geld: the trail names the property, the tabs stay those of Geld.</summary>
    private static async Task<PageContext?> LoanAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var loan = await db.Set<Loan>().AsNoTracking().Where(d => d.Id == id).Select(d => new { d.Bank, d.PropertyId }).SingleOrDefaultAsync(cancellationToken);
        if (loan is null || await PropertyAsync(db, loan.PropertyId, cancellationToken) is not { } property)
        {
            return null;
        }

        return new([.. property.Trail, new($"Darlehen {loan.Bank}", $"{Root}/finanzen/darlehen/{id}")], null);
    }

    private static async Task<PageContext?> PersonAsync(MietHahnDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var person = await db.Set<Person>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return person is null ? null : new([People, new(person.DisplayName, $"{Root}/personen/{id}")], null);
    }

    private static TrailStep PropertyStep(Guid id, string name) => new(name, $"{Root}/objekte/{id}");
}
