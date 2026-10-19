using System.ComponentModel.DataAnnotations;
using MietHahn.Domain.Metering;

namespace MietHahn.Application.Metering;

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

    /// <summary>Einheit oder Zimmer, dem der Zähler zugeordnet ist; leer = Hauszähler.</summary>
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

    [Required(ErrorMessage = "Bitte das Einbaudatum angeben.")]
    public DateOnly? InstalledOn { get; set; }

    [Required(ErrorMessage = "Bitte den Anfangsstand angeben.")]
    public decimal? InitialReading { get; set; }
}

public sealed class ReadingInput
{
    [Required(ErrorMessage = "Bitte das Ablesedatum angeben.")]
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Bitte den Zählerstand angeben.")]
    public decimal? Value { get; set; }

    public ReadingType Kind { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>Warnungen (Rückwärtslauf, Ausreißer) bestätigt: trotzdem speichern.</summary>
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
