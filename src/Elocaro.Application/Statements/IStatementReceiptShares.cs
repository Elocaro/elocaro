using Elocaro.Application.Common;

namespace Elocaro.Application.Statements;

/// <summary>
/// Which receipts of a released statement the tenants may inspect in the portal. By default nothing is released; receipts can
/// contain data of third parties, so the landlord chooses deliberately. Withdrawing a release takes effect with the next request.
/// </summary>
public interface IStatementReceiptShares
{
    /// <summary>The receipts behind the statement's cost items (suggestion) with their release state; <c>null</c> if the statement does not exist.</summary>
    Task<ReceiptShareOverview?> OverviewAsync(Guid statementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the complete selection: the given receipts are released, all others withdrawn. Only receipts that belong to the
    /// statement can be released.
    /// </summary>
    Task<Result> SetSharedAsync(Guid statementId, IReadOnlyCollection<Guid> receiptIds, CancellationToken cancellationToken = default);
}

/// <param name="Receipts">Receipts with at least one cost item in the frozen statement.</param>
public sealed record ReceiptShareOverview(Guid StatementId, IReadOnlyList<ReceiptShareRow> Receipts);

/// <param name="Amount">Sum of the receipt's cost items that enter the statement.</param>
/// <param name="CostTypes">Names of the cost types of those items.</param>
/// <param name="FileCount">Number of files (pages) of the receipt; 0 = nothing to show to the tenants.</param>
public sealed record ReceiptShareRow(
    Guid ReceiptId,
    string Supplier,
    string? InvoiceNumber,
    DateOnly InvoiceDate,
    decimal Amount,
    IReadOnlyList<string> CostTypes,
    int FileCount,
    bool Shared,
    DateTimeOffset? SharedAt,
    string? SharedBy);
