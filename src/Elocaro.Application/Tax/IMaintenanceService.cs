using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Tax;

namespace Elocaro.Application.Tax;

/// <summary>Maintenance expenses (Erhaltungsaufwand) of a property: deduct immediately or spread over 2–5 years (§ 82b EStDV).</summary>
public interface IMaintenanceService
{
    /// <summary>Maintenance items with their treatment; <c>null</c> if the property does not exist.</summary>
    Task<MaintenanceOverview?> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    /// <summary>Spreads a paid maintenance item over the given number of years (replaces an existing election).</summary>
    Task<Result> DistributeAsync(Guid propertyId, DistributionInput input, CancellationToken cancellationToken = default);

    /// <summary>Revokes the election: the item counts as deducted immediately again.</summary>
    Task<Result> RevokeAsync(Guid propertyId, Guid costItemId, CancellationToken cancellationToken = default);

    /// <summary>Deductible amounts of a year: immediate items paid in the year plus the instalments of distributions.</summary>
    Task<MaintenanceDeduction> DeductionAsync(Guid propertyId, int year, CancellationToken cancellationToken = default);
}

public sealed class DistributionInput
{
    [Required(ErrorMessage = "Bitte eine Position wählen.")]
    public Guid? CostItemId { get; set; }

    [Range(MaintenanceDistribution.MinYears, MaintenanceDistribution.MaxYears, ErrorMessage = "Bitte 2 bis 5 Jahre angeben.")]
    public int Years { get; set; } = 3;
}

/// <summary>Maintenance cost item. <c>Years</c> is <c>null</c> for immediate deduction; <c>Year</c> is the year of payment (<c>null</c> while unpaid).</summary>
public sealed record MaintenanceItem(
    Guid CostItemId,
    Guid ReceiptId,
    string Description,
    DateOnly InvoiceDate,
    DateOnly? PaidOn,
    decimal Amount,
    int? Years,
    IReadOnlyList<MaintenanceInstalment> Schedule);

public sealed record MaintenanceOverview(Guid PropertyId, string PropertyName, IReadOnlyList<MaintenanceItem> Items, IReadOnlyList<string> Warnings);

/// <summary>One deductible portion of a year with its origin (for drilldown).</summary>
public sealed record MaintenancePortion(Guid CostItemId, Guid ReceiptId, string Description, decimal Amount, bool Distributed);

public sealed record MaintenanceDeduction(int Year, IReadOnlyList<MaintenancePortion> Portions)
{
    public decimal Total => Portions.Sum(p => p.Amount);
}
