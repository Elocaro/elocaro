using System.Security.Claims;
using Elocaro.Domain.Common;
using Elocaro.Domain.Statements;

namespace Elocaro.Application.Statements;

/// <summary>
/// The sent statements as a tenant sees them in the portal. Every method derives the tenant from the account and never trusts an id
/// from the request: a dispatch of a foreign lease, an unsent revision or a receipt that is not released is simply not found.
/// The view models contain only the tenant's own <c>TenantStatement</c> and house-wide totals, never values of other parties.
/// </summary>
public interface ITenantStatements
{
    /// <summary>Statements that were sent to the tenant's own leases, newest period first.</summary>
    Task<IReadOnlyList<PortalStatementRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>The statement of one dispatch (of an own lease); <c>null</c> otherwise.</summary>
    Task<PortalStatement?> GetAsync(ClaimsPrincipal user, Guid dispatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file of a released receipt. Checks on every call that the dispatch belongs to the tenant, that it was sent, that the
    /// receipt is released for this statement and that the file belongs to the receipt; writes the access to the access log.
    /// </summary>
    Task<Elocaro.Application.Files.OpenedFile?> OpenReceiptFileAsync(ClaimsPrincipal user, Guid dispatchId, Guid receiptId, Guid fileId, CancellationToken cancellationToken = default);
}

/// <param name="Superseded">A newer revision of the same period was sent to this lease as well.</param>
/// <param name="Balance">Positive = additional payment, negative = credit.</param>
/// <param name="LetterFileId">The letter (PDF) that was sent; it is also in the document library of the portal.</param>
public sealed record PortalStatementRow(
    Guid DispatchId,
    Guid LeaseId,
    Guid LetterFileId,
    string RentalUnit,
    string Property,
    Period BillingPeriod,
    int Revision,
    DateOnly SentOn,
    decimal Balance,
    bool Superseded);

/// <param name="Reason">Reason of the correction (only for revision 2 and later).</param>
/// <param name="Previous">Comparison with the last statement of the previous year sent to the same lease; <c>null</c> if there is none.</param>
/// <param name="Receipts">Receipts released for this statement, by cost type; empty = nothing released.</param>
public sealed record PortalStatement(
    PortalStatementRow Header,
    Period Tenancy,
    int Days,
    string? Reason,
    IReadOnlyList<PortalStatementLine> Lines,
    IReadOnlyList<PortalBalance> Balances,
    decimal Balance,
    YearComparison? Previous,
    IReadOnlyList<PortalReceiptGroup> Receipts);

/// <param name="Summary">E.g. "1.240,00 € nach Wohnfläche: Ihr Anteil 248,00 €".</param>
/// <param name="Details">Further sentences of the derivation.</param>
public sealed record PortalStatementLine(
    string CostType,
    string Name,
    bool Heating,
    string Key,
    decimal CostTotal,
    decimal Share,
    string Summary,
    IReadOnlyList<string> Details);

public sealed record PortalBalance(string Name, decimal Share, decimal PrepaymentsPaid, decimal Balance);

public sealed record PortalReceiptGroup(string CostType, string Name, IReadOnlyList<PortalReceipt> Receipts);

public sealed record PortalReceipt(Guid ReceiptId, string Supplier, string? InvoiceNumber, DateOnly InvoiceDate, decimal Amount, IReadOnlyList<PortalReceiptFile> Files);

public sealed record PortalReceiptFile(Guid FileId, int Page, string Name);
