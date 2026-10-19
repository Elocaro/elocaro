using MietHahn.Application.Bank;

namespace MietHahn.Web.Components.Pages.Admin.Bank;

/// <summary>
/// Form row of an assignment part; the target select carries "L:&lt;lease&gt;" or "C:&lt;charge&gt;". Ids and dates are
/// kept as text because the form binder rejects empty values for these types inside collections.
/// </summary>
public sealed class AssignmentPartForm
{
    public decimal? Amount { get; set; }

    public string? Target { get; set; }

    public string? CostType { get; set; }

    public string? PropertyId { get; set; }

    public string? UnitId { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    /// <summary>"" = according to the cost type, "true", "false".</summary>
    public string? Allocable { get; set; }

    public string? ReceiptId { get; set; }

    public string? Note { get; set; }

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
            PropertyId = ParseId(PropertyId),
            UnitId = ParseId(UnitId),
            From = ParseDate(From),
            To = ParseDate(To),
            Allocable = Allocable switch { "true" => true, "false" => false, _ => null },
            ReceiptId = ParseId(ReceiptId),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        };
    }

    private static Guid? ParseId(string? text) => Guid.TryParse(text, out var id) ? id : null;

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;
}
