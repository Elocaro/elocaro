using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Finance;
using Elocaro.Application.Portfolio;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Portfolio;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Portfolio;

internal sealed class PropertyKeyFiguresService(ElocaroDbContext db, IAnlageVService anlageV, ILoanService loans, ITenantAccounts accounts) : IPropertyKeyFigures
{
    /// <summary>Costs the landlord bears himself; everything else on the expense side is passed on to the tenants or is no cash expense.</summary>
    private static readonly AnlageVCategory[] NonAllocable =
        [AnlageVCategory.Maintenance, AnlageVCategory.Administration, AnlageVCategory.Travel, AnlageVCategory.BankFees, AnlageVCategory.OtherExpenses];

    private static readonly AnlageVCategory[] Allocable =
        [AnlageVCategory.AllocableOperatingCosts, AnlageVCategory.PropertyTax, AnlageVCategory.Insurance];

    public async Task<PropertyKeyFigures?> GetAsync(ClaimsPrincipal user, Guid propertyId, int year, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return null;
        }

        var report = await anlageV.GetAsync(propertyId, year, cancellationToken: cancellationToken);
        if (report is null)
        {
            return null;
        }

        decimal Expense(params AnlageVCategory[] categories) => report.Result.Expenses.Where(l => categories.Contains(l.Info.Category)).Sum(l => l.Amount);
        var financing = await loans.ExpensesAsync(propertyId, year, cancellationToken);
        var acquisition = await db.Set<PropertyAcquisition>().AsNoTracking().SingleOrDefaultAsync(a => a.PropertyId == propertyId, cancellationToken);

        var units = (await RentalUnit.GetAsync(db, cancellationToken)).Values.Where(u => u.PropertyId == propertyId && u.IsRentable).Select(u => u.Id).ToHashSet();
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.ExtraRooms).AsSplitQuery()
            .Where(v => db.Set<Elocaro.Domain.Properties.Unit>().Any(u => u.Id == v.UnitId && u.PropertyId == propertyId))
            .ToListAsync(cancellationToken);
        var vacant = units.Sum(id => PropertyKeyFigureCalculator.VacantDays(year, leases.Where(v => v.RentalUnitId == id || v.ExtraRooms.Any(r => r.RoomId == id)).Select(v => v.Term)));

        var leaseIds = leases.Select(v => v.Id).ToList();
        var due = await db.Set<Charge>().AsNoTracking()
            .Where(c => leaseIds.Contains(c.LeaseId) && c.Amount > 0 && c.Due >= new DateOnly(year, 1, 1) && c.Due <= new DateOnly(year, 12, 31))
            .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0m;
        var arrears = (await accounts.OpenItemsAsync(cancellationToken)).Where(i => i.Due.Year == year && leaseIds.Contains(i.LeaseId)).Sum(i => i.Open);

        return PropertyKeyFigureCalculator.Calculate(new PropertyFigureInput(
            year,
            report.Result.Income.Where(l => l.Info.Category == AnlageVCategory.BaseRent).Sum(l => l.Amount),
            report.Result.IncomeTotal,
            Expense(NonAllocable),
            Expense(Allocable),
            financing.Interest,
            financing.OneOffCosts,
            financing.Repayment,
            acquisition is null ? null : acquisition.PurchasePrice + acquisition.AncillaryCosts,
            units.Count * Period.CalendarYear(year).Days,
            vacant,
            due,
            arrears));
    }
}
