namespace Elocaro.Application.Tax;

/// <summary>The payments assigned to a loan as instalments. Owned by the loans; the bank module implements it, so loans do not read bank tables.</summary>
public interface ILoanBookings
{
    /// <summary>Instalments booked for the loan, oldest first. Amount is the booked (negative) amount.</summary>
    Task<IReadOnlyList<LoanBooking>> ForLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
}

/// <summary>One booked instalment: transaction, booking date, booked amount and the interest part, if known.</summary>
public sealed record LoanBooking(Guid TransactionId, DateOnly BookingDate, decimal Amount, decimal? Interest);
