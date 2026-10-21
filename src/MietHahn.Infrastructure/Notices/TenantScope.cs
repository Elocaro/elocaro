using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Notices;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Notices;

/// <summary>Who is calling: administration (all) or a tenant account (own leases only).</summary>
internal enum NoticeViewer
{
    None,
    Admin,
    Tenant,
}

internal static class TenantScope
{
    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData));

    public static Guid? UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static async Task<(NoticeViewer Viewer, List<Guid> Persons)> ViewerAsync(MietHahnDbContext db, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (IsAdmin(user))
        {
            return (NoticeViewer.Admin, []);
        }

        if (!user.IsInRole(Roles.Tenant) || UserId(user) is not { } userId)
        {
            return (NoticeViewer.None, []);
        }

        var persons = await db.Set<Person>().AsNoTracking().Where(p => p.UserId == userId && p.AnonymizedAt == null).Select(p => p.Id).ToListAsync(cancellationToken);
        return (persons.Count == 0 ? NoticeViewer.None : NoticeViewer.Tenant, persons);
    }

    /// <summary>Leases (with parties) that are running on <paramref name="day"/>.</summary>
    public static async Task<List<Lease>> CurrentLeasesAsync(MietHahnDbContext db, DateOnly day, CancellationToken cancellationToken) =>
        await db.Set<Lease>().AsNoTracking().Include(v => v.Parties)
            .Where(v => v.Term.From <= day && (v.Term.To == null || v.Term.To >= day)).ToListAsync(cancellationToken);

    /// <summary>The (property, unit) pairs of the current leases of the given persons.</summary>
    public static async Task<List<(Guid Property, Guid Unit)>> OwnUnitsAsync(MietHahnDbContext db, List<Guid> persons, DateOnly day, CancellationToken cancellationToken)
    {
        var leases = await CurrentLeasesAsync(db, day, cancellationToken);
        var properties = await PropertyOfUnitAsync(db, cancellationToken);
        return leases.Where(v => v.Tenant.Any(p => persons.Contains(p.PersonId)))
            .Select(v => (Property: properties.GetValueOrDefault(v.UnitId), Unit: v.UnitId)).Where(x => x.Property != Guid.Empty).Distinct().ToList();
    }

    /// <summary>Does the notice reach a tenant of <paramref name="unitId"/> in <paramref name="propertyId"/>?</summary>
    public static bool Reaches(Notice notice, Guid propertyId, Guid unitId) => notice.PropertyId == propertyId && notice.AppliesToUnit(unitId);

    public static async Task<Dictionary<Guid, Guid>> PropertyOfUnitAsync(MietHahnDbContext db, CancellationToken cancellationToken) =>
        await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.PropertyId, cancellationToken);
}
