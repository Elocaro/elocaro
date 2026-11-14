using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Finance;

namespace Elocaro.Application.Finance;

/// <summary>Rent reductions (§ 536 BGB) of a lease: record, acknowledge or dispute; the account follows from the status.</summary>
public interface IRentReductions
{
    Task<IReadOnlyList<RentReductionRow>> ForLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(Guid leaseId, RentReductionInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, RentReductionInput input, CancellationToken cancellationToken = default);

    /// <summary>Removes the reduction and its credits from the account.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class RentReductionInput : IValidatableObject
{
    [Required(ErrorMessage = "Bitte den Mangel beschreiben."), StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Bitte den Beginn der Minderung angeben.")]
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>Quota in percent of the gross rent; alternative to <see cref="MonthlyAmount"/>.</summary>
    [Range(typeof(decimal), "0.01", "100", ErrorMessage = "Die Quote liegt über 0 % und höchstens bei 100 %.", ParseLimitsInInvariantCulture = true)]
    public decimal? Percent { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Bitte einen Betrag über 0 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? MonthlyAmount { get; set; }

    public RentReductionStatus Status { get; set; } = RentReductionStatus.Announced;

    /// <summary>Ticket with the defect and its evidence (photos, history).</summary>
    public Guid? TicketId { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Percent is null == MonthlyAmount is null)
        {
            yield return new ValidationResult("Bitte entweder eine Quote in Prozent oder einen Monatsbetrag angeben.", [nameof(Percent), nameof(MonthlyAmount)]);
        }

        if (To < From)
        {
            yield return new ValidationResult("Das Ende liegt vor dem Beginn.", [nameof(To)]);
        }

        if (Status == RentReductionStatus.Ended && To is null)
        {
            yield return new ValidationResult("Für eine beendete Minderung bitte den letzten Tag angeben.", [nameof(To)]);
        }
    }
}

/// <param name="Granted">The reduction lowers the rent (acknowledged, or ended after it was acknowledged).</param>
/// <param name="CreditsTotal">Sum of the credits created so far (positive).</param>
/// <param name="DisputedTotal">Disputed amount up to today that is not dunned (positive; only for disputed reductions).</param>
public sealed record RentReductionRow(
    Guid Id,
    Guid LeaseId,
    string Description,
    DateOnly From,
    DateOnly? To,
    decimal? Percent,
    decimal? MonthlyAmount,
    RentReductionStatus Status,
    bool Granted,
    decimal CreditsTotal,
    decimal DisputedTotal,
    Guid? TicketId,
    string? Note);
