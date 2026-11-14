using Elocaro.Application.Common;
using Elocaro.Application.Finance;
using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Finance;

internal sealed class RentReductions(ElocaroDbContext db, ITenantAccounts accounts, TimeProvider clock) : IRentReductions
{
    public async Task<IReadOnlyList<RentReductionRow>> ForLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var reductions = await db.Set<RentReduction>().AsNoTracking().Where(r => r.LeaseId == leaseId).OrderByDescending(r => r.From).ToListAsync(cancellationToken);
        if (reductions.Count == 0)
        {
            return [];
        }

        var lease = await db.Set<Lease>().AsNoTracking().SingleAsync(v => v.Id == leaseId, cancellationToken);
        var charges = await db.Set<Charge>().AsNoTracking().Where(s => s.LeaseId == leaseId && (s.Kind == ChargeType.Rent || s.Kind == ChargeType.RentReduction)).ToListAsync(cancellationToken);
        return [.. reductions.Select(r => new RentReductionRow(
            r.Id, r.LeaseId, r.Description, r.From, r.To, r.Percent, r.MonthlyAmount, r.Status, r.Grants,
            -charges.Where(s => s.ReductionId == r.Id).Sum(s => s.Amount),
            DisputedAmounts.ByMonth(charges, [r], lease.Term, today).Values.Sum(),
            r.TicketId, r.Note))];
    }

    public Task<Result> CreateAsync(Guid leaseId, RentReductionInput input, CancellationToken cancellationToken = default) =>
        InputRules.Check(input) is { } invalid ? Task.FromResult(invalid) :
        Command.ExecuteAsync(async () =>
        {
            if (!await db.Set<Lease>().AnyAsync(v => v.Id == leaseId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var reduction = new RentReduction(leaseId, input.Description!, input.From!.Value, input.To, input.Percent, input.MonthlyAmount, input.Status, GermanTime.Today(clock.GetUtcNow()))
            {
                TicketId = input.TicketId,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            };
            db.Add(reduction);
            await db.SaveChangesAsync(cancellationToken);
            await accounts.GenerateChargesAsync(leaseId, cancellationToken);
            return reduction.Id;
        });

    public Task<Result> UpdateAsync(Guid id, RentReductionInput input, CancellationToken cancellationToken = default) =>
        InputRules.Check(input) is { } invalid ? Task.FromResult(invalid) :
        Command.ExecuteAsync(async () =>
        {
            var reduction = await db.Set<RentReduction>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            reduction.Apply(input.Description!, input.From!.Value, input.To, input.Percent, input.MonthlyAmount, input.Status, GermanTime.Today(clock.GetUtcNow()));
            reduction.TicketId = input.TicketId;
            reduction.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
            await db.SaveChangesAsync(cancellationToken);
            await accounts.GenerateChargesAsync(reduction.LeaseId, cancellationToken);
            return reduction.Id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var reduction = await db.Set<RentReduction>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            var credits = await db.Set<Charge>().Where(s => s.ReductionId == id).ToListAsync(cancellationToken);
            db.RemoveRange(credits);
            db.Remove(reduction);
            await db.SaveChangesAsync(cancellationToken);
            return reduction.Id;
        });
}
