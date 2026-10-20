using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Tax;

namespace MietHahn.Application.Tax;

/// <summary>Loans of a property with repayment plan, annual interest certificates and financing costs.</summary>
public interface ILoanService
{
    Task<IReadOnlyList<LoanListItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loan with plan; <c>null</c> if it does not exist.</summary>
    Task<LoanDetail?> GetAsync(Guid loanId, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(LoanInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid loanId, LoanInput input, CancellationToken cancellationToken = default);

    Task<Result> AddExtraPaymentAsync(Guid loanId, ExtraPaymentInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveExtraPaymentAsync(Guid loanId, Guid extraPaymentId, CancellationToken cancellationToken = default);

    /// <summary>Saves the interest certificate of a year (replaces an existing one).</summary>
    Task<Result> SaveCertificateAsync(Guid loanId, CertificateInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveCertificateAsync(Guid loanId, Guid certificateId, CancellationToken cancellationToken = default);

    Task<Result> AddFinancingCostAsync(Guid loanId, FinancingCostInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveFinancingCostAsync(Guid loanId, Guid costId, CancellationToken cancellationToken = default);

    /// <summary>Deductible financing expenses of a property in a year (interest by certificate or plan, times share, plus one-off costs).</summary>
    Task<FinancingExpenses> ExpensesAsync(Guid propertyId, int year, CancellationToken cancellationToken = default);
}

public sealed class LoanInput
{
    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? PropertyId { get; set; }

    [Required(ErrorMessage = "Bitte die Bank angeben.")]
    [StringLength(200)]
    public string Bank { get; set; } = "";

    [StringLength(100)]
    public string? LoanNumber { get; set; }

    [Required(ErrorMessage = "Bitte den Darlehensbetrag angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Der Betrag muss größer als 0 sein.")]
    public decimal? Principal { get; set; }

    [Required(ErrorMessage = "Bitte das Auszahlungsdatum angeben.")]
    public DateOnly? DisbursementDate { get; set; }

    [Required(ErrorMessage = "Bitte die Fälligkeit der ersten Rate angeben.")]
    public DateOnly? FirstPaymentDate { get; set; }

    [Required(ErrorMessage = "Bitte den Sollzins angeben.")]
    [Range(0, 30, ErrorMessage = "Der Sollzins muss zwischen 0 und 30 % liegen.")]
    public decimal? InterestRatePercent { get; set; }

    /// <summary>Instalment per period. If empty, it is derived from <see cref="InitialRepaymentPercent"/>.</summary>
    [Range(0.01, 100_000_000, ErrorMessage = "Die Rate muss größer als 0 sein.")]
    public decimal? Instalment { get; set; }

    /// <summary>Initial repayment in percent per year (alternative to the instalment).</summary>
    [Range(0.01, 100, ErrorMessage = "Die Anfangstilgung muss zwischen 0 und 100 % liegen.")]
    public decimal? InitialRepaymentPercent { get; set; }

    [Range(1, 3)]
    public int MonthsPerPeriod { get; set; } = 1;

    public DateOnly? FixedUntil { get; set; }

    [Range(0.01, 100, ErrorMessage = "Der Anteil muss zwischen 0 und 100 % liegen.")]
    public decimal? SharePercent { get; set; } = 100m;

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class ExtraPaymentInput
{
    [Required(ErrorMessage = "Bitte das Datum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Der Betrag muss größer als 0 sein.")]
    public decimal? Amount { get; set; }
}

public sealed class CertificateInput
{
    [Required(ErrorMessage = "Bitte das Jahr angeben.")]
    [Range(1950, 2100, ErrorMessage = "Bitte ein gültiges Jahr angeben.")]
    public int? Year { get; set; }

    [Required(ErrorMessage = "Bitte die Jahreszinsen laut Bescheinigung angeben.")]
    [Range(0, 1_000_000_000)]
    public decimal? Interest { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal? ClosingBalance { get; set; }
}

public sealed class FinancingCostInput
{
    [Required(ErrorMessage = "Bitte das Datum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(0.01, 1_000_000_000, ErrorMessage = "Der Betrag muss größer als 0 sein.")]
    public decimal? Amount { get; set; }

    public FinancingCostKind Kind { get; set; } = FinancingCostKind.Disagio;

    [Required(ErrorMessage = "Bitte eine Beschreibung angeben.")]
    [StringLength(200)]
    public string Description { get; set; } = "";
}

public sealed record LoanListItem(Guid Id, string Bank, string? LoanNumber, Guid PropertyId, string PropertyName, decimal Principal, decimal InterestRatePercent, DateOnly? FixedUntil);

public sealed record ExtraPaymentRow(Guid Id, DateOnly Date, decimal Amount);

public sealed record CertificateRow(Guid Id, int Year, decimal Interest, decimal? ClosingBalance);

public sealed record FinancingCostRow(Guid Id, DateOnly Date, decimal Amount, FinancingCostKind Kind, string Description);

/// <summary>Interest of a year: calculated by plan and, if available, from the bank certificate (which wins).</summary>
public sealed record LoanYearInterest(int Year, decimal PlanInterest, decimal? CertificateInterest, decimal Repayment, decimal ClosingBalance)
{
    public decimal Interest => CertificateInterest ?? PlanInterest;
}

public sealed record LoanDetail(
    Guid Id,
    string PropertyName,
    LoanInput Input,
    LoanPlan Plan,
    IReadOnlyList<LoanYearInterest> Years,
    IReadOnlyList<ExtraPaymentRow> ExtraPayments,
    IReadOnlyList<CertificateRow> Certificates,
    IReadOnlyList<FinancingCostRow> Costs);

public sealed record FinancingExpenses(decimal Interest, decimal OneOffCosts)
{
    public decimal Total => Interest + OneOffCosts;
}
