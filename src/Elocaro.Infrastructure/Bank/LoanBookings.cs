using Elocaro.Application.Tax;
using Elocaro.Domain.Bank;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Bank;

internal sealed class LoanBookings(ElocaroDbContext db) : ILoanBookings
{
    public async Task<IReadOnlyList<LoanBooking>> ForLoanAsync(Guid loanId, CancellationToken cancellationToken = default) =>
        await (from a in db.Set<TransactionAssignment>().AsNoTracking()
               join u in db.Set<Transaction>().AsNoTracking() on a.TransactionId equals u.Id
               where a.LoanId == loanId
               orderby u.BookingDate
               select new LoanBooking(u.Id, u.BookingDate, a.Amount, a.LoanInterest)).ToListAsync(cancellationToken);
}
