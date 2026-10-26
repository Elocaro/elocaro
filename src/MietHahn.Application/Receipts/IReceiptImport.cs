namespace MietHahn.Application.Receipts;

/// <summary>
/// Import of a cash book or cost list (CSV with date, group, recipient, amount and note) as receipts with one cost item each.
/// The groups of the file are assigned to cost types; rows of groups without a cost type are skipped. Excel files are saved as CSV first.
/// </summary>
public interface IReceiptImport
{
    /// <param name="groupCostTypes">Cost type per group of the file; groups missing here use the suggestion.</param>
    Task<ReceiptImportPreview> PreviewAsync(Guid propertyId, byte[] csv, IReadOnlyDictionary<string, string?>? groupCostTypes = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a receipt for every new row of the preview.</summary>
    Task<ReceiptImportOutcome> ImportAsync(Guid propertyId, byte[] csv, IReadOnlyDictionary<string, string?>? groupCostTypes = null, CancellationToken cancellationToken = default);
}

public enum ReceiptRowStatus
{
    New = 0,

    /// <summary>A receipt with the same date, recipient and amount exists already (re-importing a file is harmless).</summary>
    Duplicate = 1,

    /// <summary>The group has no cost type.</summary>
    NoCostType = 2,

    /// <summary>Date or amount not readable.</summary>
    Invalid = 3,
}

public sealed record ReceiptImportRow(int Line, DateOnly? Date, string Group, string Recipient, decimal? Amount, string? Note, string? CostType, ReceiptRowStatus Status, string? Problem);

/// <param name="Suggestion">Cost type guessed from the name of the group, or <c>null</c>.</param>
public sealed record ReceiptImportGroup(string Name, int Rows, decimal Total, string? Suggestion, string? CostType);

public sealed record ReceiptImportPreview(IReadOnlyList<ReceiptImportRow> Rows, IReadOnlyList<ReceiptImportGroup> Groups, IReadOnlyList<string> Errors)
{
    public int New => Rows.Count(r => r.Status == ReceiptRowStatus.New);

    public bool CanImport => Errors.Count == 0 && New > 0;
}

public sealed record ReceiptImportOutcome(bool Succeeded, IReadOnlyList<string> Errors, int Imported, int Skipped);
