using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Finance;
using MietHahn.Application.Portfolio;
using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Portfolio;

internal sealed class PortfolioFiguresService(MietHahnDbContext db, ITenantAccounts accounts, TimeProvider clock) : IPortfolioFigures
{
    public async Task<PortfolioFigures?> FiguresAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return null;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var rentable = (await RentalUnit.GetAsync(db, cancellationToken)).Values.Where(u => u.IsRentable).Select(u => u.Id).ToHashSet();
        if (rentable.Count == 0)
        {
            return null;
        }

        var running = (await db.Set<Lease>().AsNoTracking().Include(v => v.Components).Include(v => v.ExtraRooms).AsSplitQuery().ToListAsync(cancellationToken))
            .Where(v => v.StatusAt(today) is LeaseStatus.Active or LeaseStatus.Terminated)
            .ToList();
        var month = new DateOnly(today.Year, today.Month, 1);
        var paid = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.Date >= month && p.Date < month.AddMonths(1) && p.Amount > 0)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var open = (await accounts.OpenItemsAsync(cancellationToken)).Where(i => i.Due <= today).ToList();

        var from = month.AddMonths(-11);
        var allCharges = await db.Set<Charge>().AsNoTracking().Where(c => c.Due < month.AddMonths(1)).Select(c => new { c.Due, c.Amount }).ToListAsync(cancellationToken);
        var allPayments = await db.Set<Payment>().AsNoTracking().Where(p => p.Date < month.AddMonths(1)).Select(p => new { p.Date, p.Amount }).ToListAsync(cancellationToken);
        var history = Enumerable.Range(0, 12).Select(i =>
        {
            var start = from.AddMonths(i);
            var end = start.AddMonths(1);
            return new MonthFigure(
                start,
                allCharges.Where(c => c.Due >= start && c.Due < end && c.Amount > 0).Sum(c => c.Amount),
                allPayments.Where(p => p.Date >= start && p.Date < end && p.Amount > 0).Sum(p => p.Amount),
                allCharges.Where(c => c.Due < end).Sum(c => c.Amount) - allPayments.Where(p => p.Date < end).Sum(p => p.Amount));
        }).ToList();
        var costs = (await db.Set<MietHahn.Domain.Receipts.Receipt>().AsNoTracking().Include(r => r.Items).AsSplitQuery().ToListAsync(cancellationToken))
            .SelectMany(r => r.Items).GroupBy(i => i.BillingPeriod.From.Year).Where(g => g.Key >= today.Year - 3)
            .OrderBy(g => g.Key).Select(g => new YearCost(g.Key, g.Sum(i => i.Amount))).ToList();

        return new PortfolioFigures(
            month,
            rentable.Count,
            running.SelectMany(v => v.ExtraRooms.Select(r => r.RoomId).Prepend(v.RentalUnitId)).Where(rentable.Contains).Distinct().Count(),
            running.Sum(v => v.MonthlyRentOn(today)),
            paid,
            open.Sum(i => i.Open),
            open.Select(i => i.LeaseId).Distinct().Count(),
            history,
            costs);
    }
}
