using System.ComponentModel.DataAnnotations;
using Elocaro.Domain.Metering;

namespace Elocaro.Application.Metering;

public sealed class MeterInput
{
    public MeterType Type { get; set; }

    public MeasurementUnit? MeasurementUnit { get; set; }

    [Required(ErrorMessage = "Bitte eine Bezeichnung angeben, z. B. „Kaltwasser Bad“.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte die Zählernummer angeben.")]
    [StringLength(50)]
    public string Number { get; set; } = "";

    [StringLength(200)]
    public string? Location { get; set; }

    /// <summary>Unit or room to which the meter is assigned; empty = house meter.</summary>
    public Guid? AssignmentId { get; set; }

    public Guid? MainMeterId { get; set; }

    public DateOnly? CalibrationDueDate { get; set; }

    [Range(0, 999)]
    public int Order { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class MeterCreation
{
    public MeterInput Data { get; set; } = new();

    /// <summary>Installation date; required for a physical meter, optional for a calculated meter.</summary>
    public DateOnly? InstalledOn { get; set; }

    /// <summary>Reading at installation; required for a physical meter, not used for a calculated meter.</summary>
    public decimal? InitialReading { get; set; }

    /// <summary>Not empty: a calculated meter, whose consumption is the signed sum of the consumption of these meters.</summary>
    public List<MeterTermInput> Terms { get; set; } = [];
}

/// <param name="MeterId">Source meter of a calculated meter.</param>
/// <param name="Subtract"><c>true</c> = the consumption is subtracted, otherwise added.</param>
public sealed record MeterTermInput(Guid MeterId, bool Subtract);

public sealed class ReadingInput
{
    [Required(ErrorMessage = "Bitte das Ablesedatum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Zählerstand angeben.")]
    public decimal? Value { get; set; }

    public ReadingType Kind { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>Warnings (backward running, outliers) confirmed: save anyway.</summary>
    public bool Confirmed { get; set; }
}

public sealed class ReplacementInput
{
    [Required(ErrorMessage = "Bitte das Tauschdatum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Endstand des alten Zählers angeben.")]
    public decimal? FinalReading { get; set; }

    [Required(ErrorMessage = "Bitte die Nummer des neuen Zählers angeben.")]
    [StringLength(50)]
    public string NewNumber { get; set; } = "";

    [Required(ErrorMessage = "Bitte den Anfangsstand des neuen Zählers angeben.")]
    public decimal? InitialReading { get; set; }

    public DateOnly? CalibrationDueDate { get; set; }
}
