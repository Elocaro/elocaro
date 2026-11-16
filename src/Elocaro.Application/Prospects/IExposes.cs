using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Prospects;

namespace Elocaro.Application.Prospects;

/// <summary>The exposé of a unit for prospects (PDF from the data of the unit and the house) and the hints for the new letting (rent brake).</summary>
public interface IExposes
{
    /// <summary>Hints for the asking rent: rent brake, previous rent, cap limit. <c>null</c> if the unit is unknown (or a room of a shared flat).</summary>
    Task<NewLetCheck?> CheckAsync(Guid unitId, ExposeInput input, CancellationToken cancellationToken = default);

    /// <summary>The exposé as PDF. The hints are for the landlord and are not printed; a previous rent is printed only if entered.</summary>
    Task<ExposeFile> CreateAsync(Guid unitId, ExposeInput input, CancellationToken cancellationToken = default);
}

public sealed record ExposeFile(Result Result, byte[]? Content = null, string? FileName = null);

public sealed class ExposeInput
{
    [Required(ErrorMessage = "Bitte die Kaltmiete angeben.")]
    [Range(typeof(decimal), "1", "100000", ErrorMessage = "Bitte eine Kaltmiete zwischen 1 und 100.000 € angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Rent { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag in Euro angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? OperatingCosts { get; set; }

    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag in Euro angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? HeatingCosts { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Bitte einen Betrag in Euro angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? Deposit { get; set; }

    public DateOnly? AvailableFrom { get; set; }

    /// <summary>The net rent of the previous tenant; used for the rent brake and printed, because it has to be told to the tenant before the contract.</summary>
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Bitte einen Betrag in Euro angeben.", ParseLimitsInInvariantCulture = true)]
    public decimal? PreviousRent { get; set; }

    [StringLength(3000, ErrorMessage = "Höchstens 3000 Zeichen.")]
    public string? Description { get; set; }
}
