using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Common;
using MietHahn.Application.Leases;
using MietHahn.Application.Todos;
using MietHahn.Domain.Common;
using MietHahn.Domain.Deposits;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Leases;

internal sealed class Deposits(MietHahnDbContext db) : IDeposits
{
    public async Task<DepositView?> GetAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).AsSplitQuery().SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var entries = await db.Set<DepositEntry>().AsNoTracking().Where(e => e.LeaseId == leaseId).OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ToListAsync(cancellationToken);
        var unit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId);
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var names = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.DisplayName).ToListAsync(cancellationToken);
        return new DepositView(
            lease.Id,
            unit?.Name ?? "",
            string.Join(" und ", names),
            DepositAccount.Of(lease.Deposit, entries),
            lease.DepositNote,
            lease.End,
            entries.Select(e => new DepositRow(e.Id, e.Kind, e.Date, e.Amount, e.Note)).ToList());
    }

    public Task<Result> AddAsync(Guid leaseId, DepositEntryInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var lease = await db.Set<Lease>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken) ?? throw new NotFoundException();
            var entry = new DepositEntry(leaseId, input.Kind, input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."), input.Amount ?? 0m, input.Note);
            var account = DepositAccount.Of(lease.Deposit, await db.Set<DepositEntry>().AsNoTracking().Where(e => e.LeaseId == leaseId).ToListAsync(cancellationToken));
            if (input.Kind is DepositEntryKind.Refund or DepositEntryKind.Deduction && entry.Amount > account.Balance)
            {
                throw new RuleViolationException($"Es sind nur {account.Balance:0.00} € Kaution vorhanden; mehr lässt sich nicht zurückzahlen oder verrechnen.");
            }

            db.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entry.Id;
        });

    public Task<Result> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var entry = await db.Set<DepositEntry>().SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken) ?? throw new NotFoundException();
            var others = await db.Set<DepositEntry>().AsNoTracking().Where(e => e.LeaseId == entry.LeaseId && e.Id != entryId).ToListAsync(cancellationToken);
            if (DepositAccount.Of(null, others).Balance < 0)
            {
                throw new RuleViolationException("Ohne diese Buchung wären Rückzahlungen und Verrechnungen höher als die eingegangene Kaution. Bitte zuerst diese löschen.");
            }

            db.Remove(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entry.LeaseId;
        });
}

/// <summary>
/// Dashboard: a deposit that is not complete although the third instalment (§ 551 Abs. 2 BGB: start, +1 month, +2 months) is past, and a
/// deposit that still sits with the landlord a while after the lease ended.
/// </summary>
internal sealed class DepositTodos(MietHahnDbContext db, IModuleState modules, TimeProvider clock) : ITodoSource
{
    /// <summary>Usual time the landlord takes to check for claims before paying back; not a statutory period.</summary>
    private const int RefundMonths = 3;

    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!modules.IsOptionEnabled(AppOption.LeaseDeposit)
            || (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.MasterData))))
        {
            return [];
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking().Where(v => v.Deposit != null && v.Deposit > 0).ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            return [];
        }

        var ids = leases.Select(v => v.Id).ToList();
        var entries = (await db.Set<DepositEntry>().AsNoTracking().Where(e => ids.Contains(e.LeaseId)).ToListAsync(cancellationToken)).ToLookup(e => e.LeaseId);
        var units = await RentalUnit.GetAsync(db, cancellationToken);
        var result = new List<TodoItem>();
        foreach (var lease in leases.OrderBy(v => v.Start))
        {
            var account = DepositAccount.Of(lease.Deposit, entries[lease.Id]);
            var unit = units.GetValueOrDefault(lease.RentalUnitId)?.Name ?? "";
            var link = $"/verwaltung/vertraege/{lease.Id}";
            if (account.Outstanding > 0 && lease.Start.AddMonths(2) < today && (lease.End is null || lease.End >= today))
            {
                result.Add(new TodoItem("Kaution", $"Kaution offen: {unit} ({account.Outstanding:0.00} €)", link, Urgency.Soon,
                    "Die dritte Rate (zwei Monate nach Mietbeginn) ist überschritten. Eingang buchen oder beim Mieter nachfragen."));
            }

            if (lease.End is { } end && end.AddMonths(RefundMonths) <= today && account.Balance > 0)
            {
                result.Add(new TodoItem("Kaution", $"Kaution zurückzahlen oder verrechnen: {unit} ({account.Balance:0.00} €)", link, Urgency.Info,
                    "Das Mietverhältnis ist seit mehr als drei Monaten beendet. Nach der Prüfung offener Ansprüche (Abrechnung, Schäden) auszahlen oder verrechnen.", end.AddMonths(RefundMonths)));
            }
        }

        return result;
    }
}
