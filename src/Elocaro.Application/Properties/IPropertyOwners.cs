using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;

namespace Elocaro.Application.Properties;

/// <summary>Owners of a property with their shares (co-ownership by fractions) and the period they are valid for.</summary>
public interface IPropertyOwners
{
    Task<PropertyOwnership> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(Guid propertyId, OwnerInput input, CancellationToken cancellationToken = default);

    /// <summary>Corrects an entry (person, share, period).</summary>
    Task<Result> UpdateAsync(Guid propertyId, Guid ownerId, OwnerInput input, CancellationToken cancellationToken = default);

    /// <summary>Ends an ownership on the given day (inclusive); the entry stays as history.</summary>
    Task<Result> EndAsync(Guid propertyId, Guid ownerId, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Removes an entry entered by mistake.</summary>
    Task<Result> RemoveAsync(Guid propertyId, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>The properties a person owns today (with the current share).</summary>
    Task<IReadOnlyList<OwnedProperty>> OwnedByAsync(Guid personId, CancellationToken cancellationToken = default);
}

public sealed class OwnerInput
{
    [Required(ErrorMessage = "Bitte die Person wählen.")]
    public Guid? PersonId { get; set; }

    [Required(ErrorMessage = "Bitte den Anteil angeben.")]
    [Range(0.01, 100, ErrorMessage = "Der Anteil muss zwischen 0,01 und 100 % liegen.")]
    public decimal? SharePercent { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }
}

public sealed record OwnerRow(Guid Id, Guid PersonId, string Name, decimal SharePercent, DateOnly? ValidFrom, DateOnly? ValidTo, bool Current);

/// <param name="Hints">E.g. "Die Anteile ergeben zusammen 75 %".</param>
public sealed record PropertyOwnership(IReadOnlyList<OwnerRow> Owners, IReadOnlyList<string> Hints);

public sealed record OwnedProperty(Guid PropertyId, string Name, decimal SharePercent);
