using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Prospects;

namespace Elocaro.Application.Prospects;

/// <summary>People interested in a rental unit: list, status, viewing and the step to a tenant.</summary>
public interface IProspects
{
    /// <summary>All prospects, the open ones first; optionally only for one unit.</summary>
    Task<IReadOnlyList<ProspectRow>> ListAsync(Guid? rentalUnitId = null, CancellationToken cancellationToken = default);

    Task<ProspectInput?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(ProspectInput input, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid id, ProspectInput input, CancellationToken cancellationToken = default);

    /// <summary>Sets the status. A rejection sets the day the data is deleted (<see cref="Prospect.RetentionMonthsAfterRejection"/> months).</summary>
    Task<Result> SetStatusAsync(Guid id, ProspectStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a person from the prospect (name, email, phone), marks the prospect as accepted and returns the person id in <see cref="Result.Id"/>;
    /// the person is then chosen as tenant in the lease assistant.
    /// </summary>
    Task<Result> AdoptAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ProspectInput
{
    [Required(ErrorMessage = "Bitte das Mietobjekt wählen.")]
    public Guid? RentalUnitId { get; set; }

    [Required(ErrorMessage = "Bitte den Namen des Interessenten angeben.")]
    [StringLength(Prospect.MaxName, ErrorMessage = "Der Name darf höchstens 200 Zeichen lang sein.")]
    public string Name { get; set; } = "";

    [OptionalEmail]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    public DateOnly? ViewingOn { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}

/// <param name="DeleteOn">Day the personal data is deleted: set by a rejection or a takeover, otherwise six months after the last change.</param>
public sealed record ProspectRow(
    Guid Id,
    Guid RentalUnitId,
    string RentalUnit,
    string Property,
    string Name,
    string? Email,
    string? Phone,
    ProspectStatus Status,
    DateOnly? ViewingOn,
    DateOnly DeleteOn,
    Guid? PersonId);
