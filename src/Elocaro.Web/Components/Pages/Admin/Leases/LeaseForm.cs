using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Elocaro.Application.Leases;
using Elocaro.Domain.Leases;

namespace Elocaro.Web.Components.Pages.Admin.Leases;

/// <summary>Flat form model of the new lease (a single level so field names and validation stay simple).</summary>
public sealed class LeaseForm : IValidatableObject
{
    /// <summary>The wizard step being filled in: required fields of later steps are not checked yet.</summary>
    public int Stage { get; set; }

    public Guid? RentalUnitId { get; set; }

    public DateOnly? Start { get; set; }

    public DateOnly? FixedTermUntil { get; set; }

    public Guid? Tenant1 { get; set; }

    public Guid? Tenant2 { get; set; }

    public Guid? Guarantor { get; set; }

    public BillingType BillingType { get; set; }

    public RentModel RentModel { get; set; }

    /// <summary>From <c>&lt;input type="month"&gt;</c> as "2026-01".</summary>
    public string? IndexBaseMonth { get; set; }

    public decimal? IndexBaseValue { get; set; }

    public bool HeatingCostsConsumptionBased { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? BaseRent { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? OperatingCostPrepayment { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? HeatingCostPrepayment { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? OperatingCostFlatRate { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int People { get; set; } = 1;

    [Range(1, 10, ErrorMessage = "Bitte einen Werktag zwischen 1 und 10 angeben.")]
    public int DueOnWorkingDay { get; set; } = 3;

    public PaymentMethod PaymentMethod { get; set; }

    [Range(1, 24, ErrorMessage = "Bitte eine Kündigungsfrist zwischen 1 und 24 Monaten angeben.")]
    public int NoticePeriodMonths { get; set; } = 3;

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Deposit { get; set; }

    [StringLength(1000)]
    public string? DepositNote { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    /// <summary>Monthly total of rent and the costs of the chosen billing type, for the summary.</summary>
    public decimal MonthlyTotal => (BaseRent ?? 0m) + (BillingType == BillingType.Prepayment
        ? (OperatingCostPrepayment ?? 0m) + (HeatingCostPrepayment ?? 0m)
        : OperatingCostFlatRate ?? 0m);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RentalUnitId is null)
        {
            yield return new("Bitte das Mietobjekt wählen.", [nameof(RentalUnitId)]);
        }

        if (Start is null)
        {
            yield return new("Bitte den Mietbeginn angeben.", [nameof(Start)]);
        }

        if (Stage >= 1 && Tenant1 is null)
        {
            yield return new("Bitte den Mieter wählen.", [nameof(Tenant1)]);
        }

        if (Stage >= 2 && BaseRent is null)
        {
            yield return new("Bitte die Kaltmiete angeben.", [nameof(BaseRent)]);
        }
    }

    /// <summary>The service input; fields of another billing type are not shown and therefore not taken over.</summary>
    public LeaseCreation ToCreation() => new()
    {
        RentalUnitId = RentalUnitId,
        Start = Start,
        FixedTermUntil = FixedTermUntil,
        TenantIds = new[] { Tenant1, Tenant2 }.OfType<Guid>().Distinct().ToList(),
        GuarantorIds = Guarantor is { } guarantor ? [guarantor] : [],
        BillingType = BillingType,
        HeatingCostsConsumptionBased = BillingType != BillingType.Prepayment && HeatingCostsConsumptionBased,
        RentModel = RentModel,
        IndexBaseMonth = RentModel == RentModel.Index ? ParseMonth(IndexBaseMonth) : null,
        IndexBaseValue = RentModel == RentModel.Index ? IndexBaseValue : null,
        BaseRent = BaseRent,
        OperatingCostPrepayment = BillingType == BillingType.Prepayment ? OperatingCostPrepayment : null,
        HeatingCostPrepayment = BillingType == BillingType.Prepayment ? HeatingCostPrepayment : null,
        OperatingCostFlatRate = BillingType is BillingType.FlatRate or BillingType.Inclusive ? OperatingCostFlatRate : null,
        People = People,
        Data = new LeaseInput
        {
            DueOnWorkingDay = DueOnWorkingDay,
            PaymentMethod = PaymentMethod,
            NoticePeriodMonths = NoticePeriodMonths,
            BillingType = BillingType,
            HeatingCostsConsumptionBased = BillingType != BillingType.Prepayment && HeatingCostsConsumptionBased,
            Deposit = Deposit,
            DepositNote = DepositNote,
            Notes = Notes,
        },
    };

    private static DateOnly? ParseMonth(string? month) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
}
