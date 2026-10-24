using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Communication;
using MietHahn.Application.Notifications;
using MietHahn.Infrastructure.Identity;
using Quartz;

namespace MietHahn.Infrastructure.Notifications;

/// <summary>
/// Everything that is sent as a notification mail also reaches the devices of the recipients that have push switched on, as a short hint.
/// Mails that carry a secret (password reset, sign-in links) and letters are not announced.
/// </summary>
internal sealed class PushingMailSender(IMailSender inner, IPushNotifications push, UserManager<ApplicationUser> users) : IMailSender
{
    private static readonly Dictionary<string, Hint> Hints = new(StringComparer.Ordinal)
    {
        ["message-update"] = new("Neue Nachricht", "Es gibt eine neue Nachricht.", "/verwaltung/nachrichten", "/portal/nachrichten"),
        ["ticket-update"] = new("Neuigkeiten zu einer Meldung", "Eine Meldung wurde aktualisiert.", "/verwaltung/tickets", "/portal/tickets"),
        ["deadline-reminder"] = new("Anstehende Frist", "Eine Frist oder Wartung steht an.", "/verwaltung/fristen", "/verwaltung/fristen"),
        ["deadline-summary"] = new("Wochenübersicht", "Die Wochenübersicht der Fristen liegt vor.", "/verwaltung/fristen", "/verwaltung/fristen"),
        ["document-shared"] = new("Neues Dokument", "Es liegt ein neues Dokument bereit.", "/verwaltung/dokumente", "/portal/dokumente"),
        ["notice"] = new("Neuer Aushang", "Es gibt einen neuen Aushang.", "/verwaltung/aushaenge", "/portal/aushaenge"),
        ["consumption-information"] = new("Verbrauchsinformation", "Ihre Verbrauchsinformation liegt vor.", "/verwaltung", "/portal/verbrauch"),
        ["reading-reminder"] = new("Zählerstände melden", "Bitte melden Sie Ihre Zählerstände.", "/verwaltung", "/portal/zaehler"),
        ["backup-warning"] = new("Datensicherung prüfen", "Die Datensicherung braucht Ihre Aufmerksamkeit.", "/verwaltung/system/datensicherung", "/verwaltung/system/datensicherung"),
    };

    public async Task<Guid> EnqueueAsync(MailRequest job, CancellationToken cancellationToken = default)
    {
        var id = await inner.EnqueueAsync(job, cancellationToken);
        ArgumentNullException.ThrowIfNull(job);
        if (Hints.TryGetValue(job.Template, out var hint))
        {
            try
            {
                var emails = job.To.Select(a => a.Trim().ToUpperInvariant()).ToList();
                foreach (var user in await users.Users.AsNoTracking().Where(u => u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail)).ToListAsync(cancellationToken))
                {
                    var tenant = await users.IsInRoleAsync(user, Roles.Tenant);
                    await push.NotifyAsync([user.Id], hint.Title, hint.Body, tenant ? hint.PortalUrl : hint.AdminUrl, cancellationToken);
                }
            }
            catch (DbUpdateException)
            {
                // A hint must never get in the way of the mail itself.
            }
        }

        return id;
    }

    private sealed record Hint(string Title, string Body, string AdminUrl, string PortalUrl);
}

[DisallowConcurrentExecution]
internal sealed class PushDispatchJob(IPushNotifications push) : IJob
{
    public static readonly JobKey Key = new("push-dispatch", "notifications");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await push.DispatchAsync(context.CancellationToken);
    }
}
