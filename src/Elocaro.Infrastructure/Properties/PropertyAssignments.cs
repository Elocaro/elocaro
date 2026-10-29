using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

internal sealed class PropertyAssignments(ElocaroDbContext db, UserManager<ApplicationUser> userManager, AuditWriter audit) : IPropertyAssignments
{
    public async Task<IReadOnlyList<Guid>> PropertiesOfAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Set<PropertyAssignment>().Where(z => z.UserId == userId).Select(z => z.PropertyId).ToListAsync(cancellationToken);

    public Task<bool> IsAssignedAsync(Guid userId, Guid propertyId, CancellationToken cancellationToken = default) =>
        db.Set<PropertyAssignment>().AnyAsync(z => z.UserId == userId && z.PropertyId == propertyId, cancellationToken);

    public async Task<Result> SetAsync(Guid userId, IReadOnlyCollection<Guid> propertyIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(propertyIds);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failed("Das Konto wurde nicht gefunden.");
        }

        if (!await userManager.IsInRoleAsync(user, Roles.Caretaker))
        {
            return Result.Failed("Objekte lassen sich nur Hausmeistern zuordnen.");
        }

        var desired = propertyIds.Distinct().ToHashSet();
        var known = await db.Set<Property>().Where(o => desired.Contains(o.Id)).Select(o => new { o.Id, o.Name }).ToListAsync(cancellationToken);
        if (known.Count != desired.Count)
        {
            return Result.Failed("Mindestens ein Objekt wurde nicht gefunden.");
        }

        var previous = await db.Set<PropertyAssignment>().Where(z => z.UserId == userId).ToListAsync(cancellationToken);
        var before = await Names(previous.Select(z => z.PropertyId), cancellationToken);
        db.RemoveRange(previous.Where(z => !desired.Contains(z.PropertyId)));
        db.AddRange(desired.Where(id => previous.All(z => z.PropertyId != id)).Select(id => new PropertyAssignment(id, userId)));
        await db.SaveChangesAsync(cancellationToken);

        var after = string.Join(", ", known.Select(b => b.Name).Order(StringComparer.CurrentCulture));
        if (before != after)
        {
            await audit.LogAsync(nameof(ApplicationUser), userId, AuditAction.Modified, [new FieldChange("Properties", before, after)], cancellationToken);
        }

        return Result.Ok(userId);
    }

    private async Task<string> Names(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToList();
        var names = await db.Set<Property>().Where(o => list.Contains(o.Id)).Select(o => o.Name).ToListAsync(cancellationToken);
        return string.Join(", ", names.Order(StringComparer.CurrentCulture));
    }
}
