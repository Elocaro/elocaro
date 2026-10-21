using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MietHahn.Application.Common;
using MietHahn.Domain.Tickets;

namespace MietHahn.Application.Tickets;

/// <summary>
/// Recurring caretaker duties with a tick-off list as proof. The administration defines them per property;
/// a caretaker sees and ticks those of the properties assigned to them.
/// </summary>
public interface ICareRoutines
{
    /// <summary>Administration: all routines. Caretaker: those of the assigned properties.</summary>
    Task<IReadOnlyList<RoutineRow>> ListAsync(ClaimsPrincipal user, bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<Result> CreateAsync(ClaimsPrincipal user, RoutineInput input, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ticks the routine as done (today unless <paramref name="doneOn"/> is given; not in the future).</summary>
    Task<Result> CheckAsync(ClaimsPrincipal user, Guid id, DateOnly? doneOn, string? note, CancellationToken cancellationToken = default);

    /// <summary>Proof: the ticks of a routine, newest first.</summary>
    Task<IReadOnlyList<CheckRow>> ChecksAsync(ClaimsPrincipal user, Guid id, int? year, CancellationToken cancellationToken = default);

    /// <summary>Administration: proof of the ticks of a year as PDF (for the allocation of caretaker costs and § 35a EStG).</summary>
    Task<byte[]> ProofPdfAsync(ClaimsPrincipal user, int year, CancellationToken cancellationToken = default);
}

public sealed class RoutineInput
{
    [Required(ErrorMessage = "Bitte das Objekt wählen.")]
    public Guid? PropertyId { get; set; }

    [Required(ErrorMessage = "Bitte die Aufgabe benennen.")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public CareInterval Interval { get; set; } = CareInterval.Weekly;

    [StringLength(1000)]
    public string? Description { get; set; }
}

public sealed record RoutineRow(Guid Id, Guid PropertyId, string Property, string Title, string? Description, CareInterval Interval, bool Active, DateOnly? LastDone, string? LastDoneBy, DateOnly? NextDue, bool Due, int ChecksThisYear);

public sealed record CheckRow(Guid Id, DateOnly DoneOn, string DoneBy, string? Note);
