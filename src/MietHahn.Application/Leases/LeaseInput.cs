using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Leases;

/// <summary>New lease with the initial values.</summary>
public sealed class LeaseCreation
{
    [Required(ErrorMessage = "Bitte das Mietobjekt wählen.")]
    public Guid? RentalUnitId { get; set; }

    [Required(ErrorMessage = "Bitte den Mietbeginn angeben.")]
    public DateOnly? Start { get; set; }

    /// <summary>Only for fixed-term leases (§ 575 BGB).</summary>
    public DateOnly? FixedTermUntil { get; set; }

    [MinLength(1, ErrorMessage = "Bitte mindestens einen Mieter wählen.")]
    public List<Guid> TenantIds { get; set; } = [];

    public List<Guid> GuarantorIds { get; set; } = [];

    /// <summary>Further rooms of the same shared flat rented with this lease (the rental object is the first room).</summary>
    public List<Guid> AdditionalRoomIds { get; set; } = [];

    public BillingType BillingType { get; set; }

    public bool HeatingCostsConsumptionBased { get; set; }

    public RentModel RentModel { get; set; }

    /// <summary>Index rent: month of the base index.</summary>
    public DateOnly? IndexBaseMonth { get; set; }

    /// <summary>Index rent: value of the base index.</summary>
    public decimal? IndexBaseValue { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte eine Kaltmiete angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? BaseRent { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? OperatingCostPrepayment { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? HeatingCostPrepayment { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? OperatingCostFlatRate { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int People { get; set; } = 1;

    public LeaseInput Data { get; set; } = new();
}

/// <summary>Lease details that are not time-dependent.</summary>
public sealed class LeaseInput
{
    [Range(1, 10, ErrorMessage = "Bitte einen Werktag zwischen 1 und 10 angeben.")]
    public int DueOnWorkingDay { get; set; } = 3;

    public PaymentMethod PaymentMethod { get; set; }

    [Range(1, 24, ErrorMessage = "Bitte eine Kündigungsfrist zwischen 1 und 24 Monaten angeben.")]
    public int NoticePeriodMonths { get; set; } = 3;

    public BillingType BillingType { get; set; }

    public bool HeatingCostsConsumptionBased { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? Deposit { get; set; }

    [StringLength(1000)]
    public string? DepositNote { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}

public sealed class LeaseTermInput
{
    [Required(ErrorMessage = "Bitte den Mietbeginn angeben.")]
    public DateOnly? Start { get; set; }

    /// <summary>Fixed-term lease (§ 575 BGB) until this day; empty = open-ended.</summary>
    public DateOnly? FixedTermUntil { get; set; }
}

public sealed class ComponentInput
{
    public RentComponentType Kind { get; set; }

    [Required(ErrorMessage = "Bitte angeben, ab wann der Betrag gilt.")]
    public DateOnly? From { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }
}

/// <summary>Correction of an entry of the rent history.</summary>
public sealed class ComponentCorrection
{
    [Required(ErrorMessage = "Bitte den Beginn angeben.")]
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    [Required(ErrorMessage = "Bitte den Betrag angeben.")]
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag zwischen 0 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Amount { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }
}

public sealed class OccupantCountCorrection
{
    [Required(ErrorMessage = "Bitte den Beginn angeben.")]
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int Count { get; set; } = 1;
}

public sealed class OccupantCountInput
{
    [Required(ErrorMessage = "Bitte angeben, ab wann die Anzahl gilt.")]
    public DateOnly? From { get; set; }

    [Range(1, 20, ErrorMessage = "Bitte eine Personenanzahl zwischen 1 und 20 angeben.")]
    public int Count { get; set; } = 1;
}

public sealed class TerminationInput
{
    [Required(ErrorMessage = "Bitte das Eingangsdatum der Kündigung angeben.")]
    public DateOnly? ReceivedOn { get; set; }

    public TerminatingParty By { get; set; }

    /// <summary>Empty = calculate the statutory period (§ 573c BGB).</summary>
    public DateOnly? EffectiveDate { get; set; }
}

/// <summary>Tenant change, part 1: move-out (lease end, new addresses of the tenants).</summary>
public sealed class MoveOutInput
{
    [Required(ErrorMessage = "Bitte den letzten Tag des Mietverhältnisses angeben.")]
    public DateOnly? End { get; set; }

    /// <summary>New address for all tenants (for delivering the statement), optional.</summary>
    [StringLength(200)]
    public string? NewStreet { get; set; }

    [StringLength(10)]
    public string? NewPostalCode { get; set; }

    [StringLength(100)]
    public string? NewCity { get; set; }
}

public sealed class RentModelInput
{
    public RentModel Model { get; set; }

    /// <summary>Index rent: month of the base index.</summary>
    public DateOnly? IndexBaseMonth { get; set; }

    [Range(typeof(decimal), "1", "1000", ErrorMessage = "Bitte einen Indexwert wie 121,7 angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? IndexBaseValue { get; set; }
}

public sealed class RentStepInput
{
    [Required(ErrorMessage = "Bitte den Termin der Staffel angeben.")]
    public DateOnly? From { get; set; }

    [Required(ErrorMessage = "Bitte die neue Kaltmiete angeben.")]
    [Range(typeof(decimal), "0.01", "100000", ErrorMessage = "Bitte die neue Kaltmiete angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? BaseRent { get; set; }
}

public sealed class IndexAdjustmentInput
{
    /// <summary>Receipt of the adjustment declaration by the tenant; the new rent applies from the month after next.</summary>
    [Required(ErrorMessage = "Bitte angeben, wann die Erklärung zugeht.")]
    public DateOnly? DeclaredOn { get; set; }
}
