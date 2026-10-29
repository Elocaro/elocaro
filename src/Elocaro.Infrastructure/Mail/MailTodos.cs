using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Todos;
using Elocaro.Domain.Communication;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Mail;

/// <summary>Reports permanently failed emails on the dashboard (for accounts with the system permission).</summary>
internal sealed class MailTodos(ElocaroDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System)))
        {
            return [];
        }

        var count = await db.Set<OutgoingMail>().CountAsync(m => m.Status == MailStatus.Failed, cancellationToken);
        return count == 0
            ? []
            : [new TodoItem("System", count == 1 ? "1 E-Mail konnte nicht zugestellt werden" : $"{count} E-Mails konnten nicht zugestellt werden",
                "/verwaltung/system/mails", Urgency.Urgent, "Zugangsdaten des Mailservers prüfen und erneut senden.")];
    }
}
