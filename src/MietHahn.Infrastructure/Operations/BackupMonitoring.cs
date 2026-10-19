using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;
using MietHahn.Application.Communication;
using MietHahn.Application.Operations;
using MietHahn.Application.Todos;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Operations;

/// <summary>Zeigt ein Problem mit der Sicherung im Dashboard (für Konten mit Systemrecht).</summary>
internal sealed class BackupTodos(IBackupStatus status) : ITodoSource
{
    public const string Link = "/verwaltung/system/datensicherung";

    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.System)))
        {
            return [];
        }

        var state = await status.QueryAsync(cancellationToken);
        return state.NeedsAttention
            ? [new TodoItem("System", "Datensicherung prüfen", Link, Urgency.Urgent, state.Description)]
            : [];
    }
}

/// <summary>Meldet „Degraded“, wenn die letzte Sicherung fehlgeschlagen oder zu alt ist (die App bleibt erreichbar).</summary>
internal sealed class BackupHealthCheck(IBackupStatus status) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = await status.QueryAsync(cancellationToken);
        return state.NeedsAttention ? HealthCheckResult.Degraded(state.Description) : HealthCheckResult.Healthy(state.Description);
    }
}

/// <summary>Schickt allen aktiven Admins eine Warnung, solange die Sicherung nicht in Ordnung ist (einmal täglich).</summary>
internal sealed class BackupWarning(
    IBackupStatus status,
    MietHahnDbContext db,
    IMailSender mailSending,
    IOptions<ApplicationOptions> app)
{
    /// <returns>Anzahl der benachrichtigten Admins.</returns>
    public async Task<int> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var state = await status.QueryAsync(cancellationToken);
        if (!state.NeedsAttention)
        {
            return 0;
        }

        var recipients = await AdminRecipients.GetAsync(db, cancellationToken);
        if (recipients.Count == 0)
        {
            return 0;
        }

        await mailSending.EnqueueAsync(
            new MailRequest(
                recipients,
                "backup-warning",
                new
                {
                    state.Description,
                    state.Message,
                    Link = app.Value.Link(BackupTodos.Link).ToString(),
                },
                Reference: "backup"),
            cancellationToken);
        return recipients.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class BackupWarningJob(BackupWarning warning) : IJob
{
    public static readonly JobKey Key = new("backup-warning", "system");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await warning.ValidateAsync(context.CancellationToken);
    }
}
