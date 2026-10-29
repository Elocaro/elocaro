using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Leases;

namespace Elocaro.Application.Leases;

/// <summary>
/// Assistant for rent increases: demand to the comparative rent (§ 558 BGB), index adjustment (§ 557b), information about a graduated
/// step (§ 557a) and adjustment of the flat rate (§ 560 Abs. 1). It checks the preconditions, generates the letter from a template,
/// calculates the dates from the day of receipt and takes the new amount into the lease.
/// </summary>
public interface IRentIncreases
{
    Task<RentIncreaseOverview?> OverviewAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>Checks a demand to the comparative rent (limits, waiting periods, dates) without storing anything.</summary>
    Task<RentIncreaseCheck> CheckAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default);

    /// <summary>The letter as PDF with the real data; stores nothing.</summary>
    Task<Documents.TemplatePreview> PreviewAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default);

    /// <summary>Checks, generates the letter, files it as document and stores the increase as draft. <see cref="Result.Id"/> is the increase.</summary>
    Task<Result> CreateDraftAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default);

    /// <summary>The tenant received the letter. The dates are calculated from this day; index and flat rate become effective in the lease.</summary>
    Task<Result> DeclareAsync(Guid id, DateOnly received, CancellationToken cancellationToken = default);

    /// <summary>The tenant consented in time: the new base rent is entered in the lease from the effective date.</summary>
    Task<Result> AgreeAsync(Guid id, DateOnly consentedOn, CancellationToken cancellationToken = default);

    Task<Result> RefuseAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> WithdrawAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class RentIncreaseInput
{
    public RentIncreaseKind Kind { get; set; }

    [Required(ErrorMessage = "Bitte eine Vorlage wählen.")]
    public Guid? TemplateId { get; set; }

    /// <summary>Day the letter is expected to reach the tenant (default: today + 3 days).</summary>
    public DateOnly? PlannedDeclaration { get; set; }

    /// <summary>Demand to the comparative rent: new base rent. Flat rate: new flat rate. Empty = the highest permissible amount.</summary>
    public decimal? NewAmount { get; set; }

    public ComparativeBasis Basis { get; set; }

    [StringLength(200)]
    public string? RentIndexName { get; set; }

    public decimal? PerSquareMetreFrom { get; set; }

    public decimal? PerSquareMetreTo { get; set; }

    /// <summary>Rent index: the price per m² put into the range (placement of the flat).</summary>
    public decimal? PerSquareMetre { get; set; }

    [StringLength(2000)]
    public string? FeatureText { get; set; }

    public List<ComparableFlatInput> Comparables { get; set; } = [];

    [StringLength(200)]
    public string? AppraiserName { get; set; }

    public DateOnly? AppraisalDate { get; set; }

    /// <summary>Expert opinion: comparative rent of the flat per month.</summary>
    public decimal? AppraisalRent { get; set; }

    /// <summary>Flat rate: the increase is reserved in the lease.</summary>
    public bool ReservationConfirmed { get; set; }

    [StringLength(4000)]
    public string? CostReason { get; set; }

    /// <summary>Modernisation: the work and its effect (§ 555b), as shown in the letter.</summary>
    [StringLength(4000)]
    public string? ModernisationDescription { get; set; }

    /// <summary>Modernisation: costs spent for this flat (share of the whole building already apportioned).</summary>
    public decimal? ModernisationCosts { get; set; }

    /// <summary>Modernisation: share of the costs that is maintenance (not chargeable). Ignored in the simplified procedure.</summary>
    public decimal? MaintenanceShare { get; set; }

    /// <summary>Modernisation: simplified procedure up to 10,000 € (§ 559c).</summary>
    public bool Simplified { get; set; }

    /// <summary>Modernisation: the work was announced (§ 555c) and the costs are at most 10 % above the announcement.</summary>
    public bool AnnouncedProperly { get; set; } = true;
}

public sealed class ComparableFlatInput
{
    [StringLength(300)]
    public string Address { get; set; } = "";

    public decimal? Area { get; set; }

    /// <summary>Base rent per month.</summary>
    public decimal? Rent { get; set; }
}

public sealed record RentIncreaseCheck(
    decimal? ComparativeRent,
    decimal? NewAmount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateOnly? EarliestDeclaration,
    DateOnly? EffectiveFrom,
    DateOnly? ConsentDeadline,
    decimal? CapLimit,
    decimal? MaxRent)
{
    public bool IsAllowed => Errors.Count == 0;
}

/// <param name="LivingArea">Area for the rent index: living area of the flat (room: room plus share of the common areas).</param>
/// <param name="Index">Index adjustment that would be possible.</param>
public sealed record RentIncreaseOverview(
    Guid LeaseId,
    string RentalUnit,
    string Property,
    RentModel Model,
    BillingType BillingType,
    decimal? BaseRent,
    DateOnly? LastIncrease,
    decimal? RentThreeYearsAgo,
    int CapPercent,
    decimal? LivingArea,
    DateOnly EarliestDeclaration,
    decimal? FlatRate,
    IndexProposal? Index,
    RentStepView? NextStep,
    IReadOnlyList<RentIncreaseRow> Increases);

public sealed record RentIncreaseRow(
    Guid Id,
    RentIncreaseKind Kind,
    RentIncreaseStatus Status,
    decimal CurrentAmount,
    decimal NewAmount,
    DateOnly PlannedDeclaration,
    DateOnly EffectiveFrom,
    DateOnly? DeclaredOn,
    DateOnly? ConsentDeadline,
    DateOnly? ConsentOn,
    Guid? DocumentId,
    string Summary);
