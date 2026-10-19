using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Leases;

namespace MietHahn.Application.Leases;

/// <summary>Neuer Mietvertrag mit den Anfangswerten.</summary>
public sealed class LeaseCreation
{
    [Required(ErrorMessage = "Bitte das Mietobjekt wählen.")]
    public Guid? RentalUnitId { get; set; }

    [Required(ErrorMessage = "Bitte den Mietbeginn angeben.")]
    public DateOnly? Start { get; set; }

    /// <summary>Nur bei Zeitmietverträgen (§ 575 BGB).</summary>
    public DateOnly? FixedTermUntil { get; set; }

    [MinLength(1, ErrorMessage = "Bitte mindestens einen Mieter wählen.")]
    public List<Guid> TenantIds { get; set; } = [];

    public List<Guid> GuarantorIds { get; set; } = [];

    public BillingType BillingType { get; set; }

    public bool HeatingCostsConsumptionBased { get; set; }

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

/// <summary>Nicht zeitabhängige Vertragsangaben.</summary>
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

    /// <summary>Leer = gesetzliche Frist berechnen (§ 573c BGB).</summary>
    public DateOnly? EffectiveDate { get; set; }
}

/// <summary>Mieterwechsel, Teil 1: Auszug (Vertragsende, neue Anschriften der Mieter).</summary>
public sealed class MoveOutInput
{
    [Required(ErrorMessage = "Bitte den letzten Tag des Mietverhältnisses angeben.")]
    public DateOnly? End { get; set; }

    /// <summary>Neue Anschrift für alle Mieter (für die Zustellung der Abrechnung), optional.</summary>
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

    /// <summary>Indexmiete: Monat des Ausgangsindex.</summary>
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
    /// <summary>Zugang der Anpassungserklärung beim Mieter; die neue Miete gilt ab dem übernächsten Monat.</summary>
    [Required(ErrorMessage = "Bitte angeben, wann die Erklärung zugeht.")]
    public DateOnly? DeclaredOn { get; set; }
}
