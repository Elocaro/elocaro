using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;

namespace MietHahn.Application.Properties;

/// <summary>
/// Which landlord profile (sender of documents) a property has over time, e.g. after a change of owner.
/// Without entries the profile chosen on the property applies; an entry applies from its day on.
/// </summary>
public interface IPropertyLandlords
{
    Task<LandlordHistory> GetAsync(Guid propertyId, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(Guid propertyId, LandlordChangeInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid propertyId, Guid entryId, LandlordChangeInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid propertyId, Guid entryId, CancellationToken cancellationToken = default);
}

public sealed class LandlordChangeInput
{
    [Required(ErrorMessage = "Bitte das Vermieterprofil wählen.")]
    public Guid? ProfileId { get; set; }

    [Required(ErrorMessage = "Bitte angeben, ab wann das Profil gilt.")]
    public DateOnly? From { get; set; }
}

/// <param name="Current">Applies today.</param>
public sealed record LandlordEntry(Guid Id, Guid ProfileId, string ProfileName, DateOnly From, bool Current);

/// <param name="BaseProfileName">The profile that applies before the first entry (chosen on the property, else the standard profile).</param>
public sealed record LandlordHistory(string? BaseProfileName, IReadOnlyList<LandlordEntry> Entries, IReadOnlyList<(Guid Id, string Name)> Profiles);
