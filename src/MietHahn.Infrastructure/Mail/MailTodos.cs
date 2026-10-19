using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Todos;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Mail;

/// <summary>Meldet endgültig fehlgeschlagene E-Mails im Dashboard (für Konten mit Systemrecht).</summary>
internal sealed class MailTodos(MietHahnDbContext db) : ITodoSource
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
