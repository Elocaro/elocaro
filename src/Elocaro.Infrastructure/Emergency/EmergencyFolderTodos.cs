using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Todos;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>
/// Dashboard reminder to create (or renew) the emergency folder: never created, or older than 12 months. Only for accounts with
/// the system permission (who may create it), and only once there is something to hand over (a property exists).
/// </summary>
internal sealed class EmergencyFolderTodos(ElocaroDbContext db, TimeProvider clock, IOptions<DemoOptions> demo) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (demo.Value.Enabled || (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System))))
        {
            return [];
        }

        if (!await db.Set<Property>().AnyAsync(cancellationToken))
        {
            return [];
        }

        var last = await db.Set<EmergencyFolder>().AsNoTracking().Select(e => e.LastCreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            return [new TodoItem("System", "Notfallmappe erstellen", EmergencyFolderContent.PageLink, Urgency.Info,
                "Eine verschlüsselte Datei, mit der Angehörige die Vermietung weiterführen können, auch wenn Elocaro nicht mehr erreichbar ist.")];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        return GermanTime.Today(last.Value) <= today.AddYears(-1)
            ? [new TodoItem("System", "Notfallmappe erneuern", EmergencyFolderContent.PageLink, Urgency.Soon, $"Die letzte Notfallmappe ist vom {GermanTime.Today(last.Value):dd.MM.yyyy} und damit älter als ein Jahr.")]
            : [];
    }
}
