namespace Elocaro.Application.Setup;

/// <summary>
/// Takes over an existing stock from a spreadsheet: one CSV file (Excel: "CSV (Trennzeichen-getrennt)") with one row per rented
/// unit creates properties, units with living area, tenants, leases and an opening balance. Running it again does not create anything twice.
/// </summary>
public interface IDataImport
{
    /// <summary>A sample file with the columns and one example row (UTF-8 with BOM, semicolon, German numbers).</summary>
    byte[] TemplateCsv();

    /// <summary>Reads the file and says per row what would be created, reused or is wrong. Writes nothing.</summary>
    Task<DataImportPreview> PreviewAsync(byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Imports everything or nothing: with any error in the file or a row nothing is written.</summary>
    Task<DataImportResult> ImportAsync(byte[] content, CancellationToken cancellationToken = default);
}

/// <param name="FileErrors">Problems with the file as a whole (missing columns, no rows).</param>
public sealed record DataImportPreview(IReadOnlyList<DataImportRow> Rows, DataImportSummary Summary, IReadOnlyList<string> FileErrors)
{
    public bool CanImport => FileErrors.Count == 0 && Rows.Count > 0 && Rows.All(r => r.Errors.Count == 0);
}

/// <param name="Line">Line number in the file (the header is line 1).</param>
/// <param name="Actions">What happens with the row, e.g. "Objekt neu", "Einheit vorhanden", "Vertrag neu ab 01.11.2026".</param>
public sealed record DataImportRow(int Line, string Property, string Unit, string? Tenant, IReadOnlyList<string> Actions, IReadOnlyList<string> Errors);

public sealed record DataImportSummary(int NewProperties, int NewUnits, int NewPeople, int NewLeases, int OpeningBalances, int Existing)
{
    public static DataImportSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record DataImportResult(bool Succeeded, DataImportSummary Summary, IReadOnlyList<string> Errors);
