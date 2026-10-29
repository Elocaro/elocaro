using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Datev;

namespace Elocaro.Application.Tax;

/// <summary>
/// DATEV export (posting batch, EXTF) of payments received and paid receipts on a cash basis, with a configurable
/// account mapping and a log that prevents exporting an item twice.
/// </summary>
public interface IDatevExport
{
    Task<DatevSettingsInput> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves client data. If the chart changes, accounts without a custom value follow the new chart.</summary>
    Task<Result> SaveSettingsAsync(DatevSettingsInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatevAccountRow>> AccountsAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAccountsAsync(IReadOnlyDictionary<string, int> accounts, CancellationToken cancellationToken = default);

    /// <summary>Resets all accounts to the proposals of the chart.</summary>
    Task<Result> ResetAccountsAsync(CancellationToken cancellationToken = default);

    Task<DatevPreview> PreviewAsync(DateOnly from, DateOnly to, bool includeExported, CancellationToken cancellationToken = default);

    /// <summary>Creates the file, logs it and marks the items as exported. <see cref="Result.Id"/> is the export.</summary>
    Task<Result> ExportAsync(DateOnly from, DateOnly to, bool fixedPosting, bool includeExported, string exportedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatevExportRow>> ExportsAsync(CancellationToken cancellationToken = default);

    Task<DatevFile?> FileAsync(Guid exportId, CancellationToken cancellationToken = default);

    /// <summary>All bookings of the period as a plain CSV (semicolon, German numbers, UTF-8 with BOM); not logged.</summary>
    Task<byte[]> BookingsCsvAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed class DatevSettingsInput
{
    [Range(1001, 9_999_999, ErrorMessage = "Die Beraternummer muss zwischen 1001 und 9999999 liegen.")]
    public int ConsultantNumber { get; set; }

    [Range(1, 99_999, ErrorMessage = "Die Mandantennummer muss zwischen 1 und 99999 liegen.")]
    public int ClientNumber { get; set; }

    public DatevChart Chart { get; set; } = DatevChart.Skr03;

    [Range(4, 8, ErrorMessage = "Die Sachkontenlänge muss zwischen 4 und 8 liegen.")]
    public int AccountLength { get; set; } = 4;

    [Range(1, 12, ErrorMessage = "Bitte einen Monat von 1 bis 12 angeben.")]
    public int FiscalYearStartMonth { get; set; } = 1;

    [StringLength(2)]
    public string? DictationCode { get; set; }
}

public sealed record DatevAccountRow(string Key, string Label, int Account, bool IsDefault);

/// <param name="Bookings">Lines that would be exported.</param>
/// <param name="Warnings">Receipts and payments that are missing and why.</param>
/// <param name="AlreadyExported">Items that are skipped because an earlier export contains them.</param>
public sealed record DatevPreview(IReadOnlyList<DatevBooking> Bookings, IReadOnlyList<string> Warnings, int AlreadyExported, bool SettingsComplete)
{
    public decimal Total => Bookings.Sum(b => b.Amount);
}

public sealed record DatevExportRow(Guid Id, DateTimeOffset At, string By, DateOnly From, DateOnly To, int Count, decimal Total, bool FixedPosting, string FileName);

public sealed record DatevFile(string FileName, byte[] Content);
