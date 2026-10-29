using Elocaro.Application.Bank;

namespace Elocaro.Web.Components.Pages.Admin.Bank;

/// <summary>
/// Form row of an assignment part; <see cref="Target"/> carries "L:&lt;lease&gt;" or "C:&lt;charge&gt;".
/// </summary>
public sealed class AssignmentPartForm
{
    public decimal? Amount { get; set; }

    public string? Target { get; set; }

    public string? CostType { get; set; }

    public Guid? PropertyId { get; set; }

    public Guid? UnitId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary><c>null</c> = according to the cost type.</summary>
    public bool? Allocable { get; set; }

    public Guid? ReceiptId { get; set; }

    public Guid? LoanId { get; set; }

    /// <summary>Interest share of a loan instalment; <c>null</c> = according to the repayment plan.</summary>
    public decimal? LoanInterest { get; set; }

    public string? Note { get; set; }

    public AssignmentPartForm Copy() => (AssignmentPartForm)MemberwiseClone();

    public AssignmentPartInput ToInput()
    {
        Guid? lease = null;
        Guid? charge = null;
        if (Target is { Length: > 2 } && Guid.TryParse(Target.AsSpan(2), out var id))
        {
            if (Target.StartsWith("L:", StringComparison.Ordinal))
            {
                lease = id;
            }
            else if (Target.StartsWith("C:", StringComparison.Ordinal))
            {
                charge = id;
            }
        }

        return new AssignmentPartInput
        {
            Amount = Amount,
            LeaseId = lease,
            ChargeId = charge,
            CostType = string.IsNullOrWhiteSpace(CostType) ? null : CostType,
            PropertyId = PropertyId,
            UnitId = UnitId,
            From = From,
            To = To,
            Allocable = Allocable,
            ReceiptId = ReceiptId,
            LoanId = LoanId,
            LoanInterest = LoanId is null ? null : LoanInterest,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        };
    }
}
