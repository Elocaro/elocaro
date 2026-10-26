using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;
using MietHahn.Application.Finance;
using MietHahn.Domain.Common;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Admin;

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

        return new PortfolioFigures(
            month,
            rentable.Count,
            running.SelectMany(v => v.ExtraRooms.Select(r => r.RoomId).Prepend(v.RentalUnitId)).Where(rentable.Contains).Distinct().Count(),
            running.Sum(v => v.MonthlyRentOn(today)),
            paid,
            open.Sum(i => i.Open),
            open.Select(i => i.LeaseId).Distinct().Count());
    }
}
