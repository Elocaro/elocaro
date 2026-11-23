using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;
using Elocaro.Domain.Prospects;

namespace Elocaro.Application.Prospects;

/// <summary>
/// Viewing dates for a rental unit. The landlord offers dates and hands out a link; a prospect picks a free date there without an account,
/// which creates the prospect (status "Besichtigung") and fixes the date.
/// </summary>
public interface IViewings
{
    /// <summary>Dates from today on (booked ones with the name of the prospect), optionally for one unit.</summary>
    Task<IReadOnlyList<ViewingRow>> ListAsync(Guid? rentalUnitId = null, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(ViewingInput input, CancellationToken cancellationToken = default);

    /// <summary>Removes a free date; a booked one has to be cancelled with the prospect first.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A new link for the unit (an open one is withdrawn). The link is only returned here, the database keeps its hash.</summary>
    Task<ViewingLinkResult> CreateLinkAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);

    /// <summary>What a prospect sees behind a link; <c>null</c> for unknown, expired or withdrawn links (all look the same).</summary>
    Task<ViewingOffer?> OfferAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Books a free date: creates the prospect and fixes the date. A date is given once.</summary>
    Task<Result> BookAsync(string token, BookingInput input, CancellationToken cancellationToken = default);

    /// <summary>The calendar file for a date of the offer, so the prospect can save it in the calendar.</summary>
    Task<string?> CalendarFileAsync(string token, Guid slotId, CancellationToken cancellationToken = default);
}

public sealed class ViewingInput
{
    [Required(ErrorMessage = "Bitte das Mietobjekt wählen.")]
    public Guid? RentalUnitId { get; set; }

    [Required(ErrorMessage = "Bitte den Tag angeben.")]
    public DateOnly? Day { get; set; }

    [Required(ErrorMessage = "Bitte die Uhrzeit angeben.")]
    public TimeOnly? Time { get; set; }

    [Range(ViewingSlot.MinMinutes, ViewingSlot.MaxMinutes, ErrorMessage = "Die Dauer muss zwischen 10 und 240 Minuten liegen.")]
    public int Minutes { get; set; } = 20;
}

/// <param name="ProspectId">Who booked the date, <c>null</c> if it is free.</param>
public sealed record ViewingRow(Guid Id, Guid RentalUnitId, string RentalUnit, string Property, DateTimeOffset StartsAt, int Minutes, Guid? ProspectId, string? ProspectName);

public sealed record ViewingLinkResult(Result Result, string? Url = null, DateTimeOffset? ExpiresAt = null);

/// <param name="Unit">Unit and property.</param>
/// <param name="Controller">Landlord (name, address, contact) as controller under data protection law; empty if no profile exists yet.</param>
/// <param name="RetentionMonths">Months the data of a prospect without a decision is kept.</param>
public sealed record ViewingOffer(string Unit, string Controller, int RetentionMonths, IReadOnlyList<OfferedSlot> Slots);

public sealed record OfferedSlot(Guid Id, DateTimeOffset StartsAt, int Minutes);

public sealed class BookingInput
{
    [Required(ErrorMessage = "Bitte einen Termin wählen.")]
    public Guid? SlotId { get; set; }

    [Required(ErrorMessage = "Bitte Ihren Namen angeben.")]
    [StringLength(Prospect.MaxName, ErrorMessage = "Der Name darf höchstens 200 Zeichen lang sein.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Bitte eine E-Mail-Adresse oder Telefonnummer angeben.")]
    [StringLength(254)]
    public string Contact { get; set; } = "";
}
