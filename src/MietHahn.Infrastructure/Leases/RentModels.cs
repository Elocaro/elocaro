using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;
using MietHahn.Application.Communication;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Identity;
using MietHahn.Infrastructure.Persistence;
using Quartz;

namespace MietHahn.Infrastructure.Leases;

/// <summary>Übernimmt fällige Staffeln als Kaltmiete und informiert die Admins per Mail.</summary>
internal sealed class RentStepApplication(MietHahnDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    /// <returns>Anzahl der übernommenen Staffeln.</returns>
    public async Task<int> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>()
            .Include(v => v.RentSteps).Include(v => v.Components).Include(v => v.People).Include(v => v.Parties).AsSplitQuery()
            .Where(v => v.RentSteps.Any(s => s.AppliedAt == null && s.From <= today))
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return 0;
        }

        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var lines = new List<string>();
        foreach (var lease in leases)
        {
            foreach (var rentStep in lease.ApplyDueSteps(today, clock.GetUtcNow()))
            {
                var property = rentalUnits.GetValueOrDefault(lease.RentalUnitId);
                lines.Add(string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{property?.Name}, {property?.Property}: ab {rentStep.From:dd.MM.yyyy} {rentStep.BaseRent:#,##0.00} € Kaltmiete"));
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var recipients = await AdminRecipients.GetAsync(db, cancellationToken);
        if (lines.Count > 0 && recipients.Count > 0)
        {
            await mail.EnqueueAsync(new MailRequest(recipients, "graduated-rent-applied", new { RentSteps = lines, Link = app.Value.Link("/verwaltung/vertraege").ToString() }, Reference: "graduated-rent"), cancellationToken);
        }

        return lines.Count;
    }
}

[DisallowConcurrentExecution]
internal sealed class RentStepJob(RentStepApplication app) : IJob
{
    public static readonly JobKey Key = new("graduated-rent", "leases");

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await app.ApplyAsync(context.CancellationToken);
    }
}

/// <summary>Dashboard: anstehende Staffeln (60 Tage) und mögliche Indexanpassungen.</summary>
internal sealed class RentModelTodos(MietHahnDbContext db, TimeProvider clock) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData)))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var to = today.AddDays(60);
        var leases = await db.Set<Lease>().AsNoTracking()
            .Include(v => v.RentSteps).Include(v => v.Components).AsSplitQuery()
            .Where(v => v.RentModel != RentModel.Standard && (v.Term.To == null || v.Term.To >= today))
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return [];
        }

        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var newestCpi = await db.Set<CpiValue>().AsNoTracking().OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
        var todos = new List<TodoItem>();
        foreach (var lease in leases)
        {
            var name = rentalUnits.GetValueOrDefault(lease.RentalUnitId)?.Name ?? "Mietobjekt";
            var link = $"/verwaltung/vertraege/{lease.Id}";
            foreach (var rentStep in lease.RentSteps.Where(s => s.AppliedAt is null && s.From <= to))
            {
                todos.Add(new TodoItem("Mieten", $"Staffel {name}: {rentStep.BaseRent.ToString("#,##0.00", CultureInfo.GetCultureInfo("de-DE"))} € ab {rentStep.From:dd.MM.yyyy}", link, Urgency.Info,
                    "Wird am Stichtag automatisch als Kaltmiete übernommen.", rentStep.From));
            }

            if (lease.RentModel == RentModel.Index && lease.IndexBaseValue is { } baseValue && newestCpi is not null
                && newestCpi.Month > lease.IndexBaseMonth && newestCpi.Value > baseValue)
            {
                var earliest = IndexedRent.EarliestEffectiveDate(lease.LastBaseRentChange() ?? lease.Start);
                if (IndexedRent.EffectiveFrom(today) >= earliest)
                {
                    todos.Add(new TodoItem("Mieten", $"Indexanpassung möglich: {name} (+{IndexedRent.ChangePercent(baseValue, newestCpi.Value).ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))} %)", link, Urgency.Soon,
                        "Erklärung in Textform; die neue Miete gilt ab dem übernächsten Monat."));
                }
            }
        }

        return todos;
    }
}
