using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Todos;
using Elocaro.Domain.Communication;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Mail;

/// <summary>
/// Reports permanently failed emails on the dashboard (for accounts with the system permission). A failed mail whose content was removed
/// cannot be resent and would stay forever: it is reported for a week, long enough to notice a broken mail server. Mails failed
/// by anonymizing a person are not reported: they say nothing about the mail server and need no action.
/// </summary>
internal sealed class MailTodos(ElocaroDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System)))
        {
            return [];
        }

        var purgedSince = clock.GetUtcNow().AddDays(-7);
        var count = await db.Set<OutgoingMail>().CountAsync(
            m => m.Status == MailStatus.Failed && m.Subject != OutgoingMail.AnonymizedSubject && (m.ContentPurgedAt == null || m.ContentPurgedAt > purgedSince),
            cancellationToken);
        return count == 0
            ? []
            : [new TodoItem("System", count == 1 ? "1 E-Mail konnte nicht zugestellt werden" : $"{count} E-Mails konnten nicht zugestellt werden",
                "/verwaltung/system/mails", Urgency.Urgent, "Zugangsdaten des Mailservers prüfen und erneut senden. Einladungen und Anmeldelinks neu auslösen.")];
    }
}
