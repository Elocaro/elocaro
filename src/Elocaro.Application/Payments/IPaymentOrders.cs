namespace Elocaro.Application.Payments;

/// <summary>
/// Payments the landlord still has to make, and the SEPA transfer file for them (<c>pain.001.001.09</c>). The file is uploaded in the online
/// banking of the landlord; Elocaro books nothing and sends nothing to a bank. Whether it was paid is seen later in the bank import.
/// </summary>
public interface IPaymentOrders
{
    /// <summary>Open receipts, credits of tenants and deposits to pay back, with the reason if one cannot be paid yet (no IBAN). Newest due date first.</summary>
    Task<IReadOnlyList<PaymentCandidate>> CandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The file for the chosen candidates (their <see cref="PaymentCandidate.Key"/>). Amount and recipient are read again from the data, never taken from the
    /// request. Every person whose IBAN goes into the file is logged as an access.
    /// </summary>
    Task<PaymentOrderResult> CreateFileAsync(IReadOnlyList<string> keys, DateOnly executionDate, CancellationToken cancellationToken = default);
}

public enum PaymentSource
{
    /// <summary>An open receipt (invoice of a tradesperson or supplier).</summary>
    Receipt = 0,

    /// <summary>Credit of a tenant account, e.g. from the utility cost statement.</summary>
    TenantCredit = 1,

    /// <summary>Deposit to be paid back after the end of the lease.</summary>
    Deposit = 2,
}

/// <param name="Key">Stable identification for the selection, e.g. <c>receipt:{id}</c>.</param>
/// <param name="Payee">Who gets the money (name on the account).</param>
/// <param name="Purpose">Text of the transfer.</param>
/// <param name="Reference">What it is about for the user (property, apartment, invoice).</param>
/// <param name="DueOn">Due date of the receipt, if there is one.</param>
/// <param name="Problem">Why it cannot be put into a file yet (German), otherwise <c>null</c>.</param>
/// <param name="Notice">A hint to check before paying (German), otherwise <c>null</c>.</param>
public sealed record PaymentCandidate(string Key, PaymentSource Source, string Payee, string Purpose, string Reference, decimal Amount, DateOnly? DueOn, string? Problem, string? Notice = null);

public sealed record PaymentOrderResult(bool Succeeded, IReadOnlyList<string> Errors, string? FileName = null, byte[]? Content = null, int Count = 0, decimal Total = 0m)
{
    public static PaymentOrderResult Failed(params string[] errors) => new(false, errors);
}
