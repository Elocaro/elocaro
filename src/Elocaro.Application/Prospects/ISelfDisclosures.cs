using System.ComponentModel.DataAnnotations;
using Elocaro.Application.Common;

namespace Elocaro.Application.Prospects;

/// <summary>
/// The self-disclosure of a prospect: the landlord creates a link, the prospect fills in the form without an account.
/// The answers belong to the prospect and are deleted with it. The form informs under Art. 13 GDPR; it asks no consent (Art. 6 (1) b).
/// </summary>
public interface ISelfDisclosures
{
    /// <summary>
    /// A new link for the prospect (an open one is withdrawn). The link is only returned here, the database keeps its hash.
    /// </summary>
    Task<DisclosureLink> CreateLinkAsync(Guid prospectId, int days, CancellationToken cancellationToken = default);

    /// <summary>The latest self-disclosure of the prospect; reading the answers is logged as an access.</summary>
    Task<DisclosureView?> GetAsync(Guid prospectId, CancellationToken cancellationToken = default);

    /// <summary>What the prospect sees behind a link; <c>null</c> for unknown, expired, withdrawn or used links (all look the same).</summary>
    Task<DisclosureForm?> ForFormAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Stores the answers and moves the prospect to the status "Selbstauskunft". A link works once.</summary>
    Task<Result> SubmitAsync(string token, DisclosureInput input, CancellationToken cancellationToken = default);
}

public sealed record DisclosureLink(Result Result, string? Url = null, DateTimeOffset? ExpiresAt = null);

/// <param name="Open">The link works (not expired, not withdrawn, not used).</param>
public sealed record DisclosureView(
    Guid Id,
    bool Open,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? SubmittedAt,
    int? Persons,
    string? Occupation,
    decimal? NetIncome,
    string? Pets,
    DateOnly? MoveInOn,
    string? Message);

/// <param name="Unit">Unit and property the prospect applies for.</param>
/// <param name="Controller">Landlord (name, address, contact) as controller under data protection law; empty if no profile exists yet.</param>
/// <param name="RetentionMonths">Months the data is kept after a rejection.</param>
public sealed record DisclosureForm(string Unit, DateTimeOffset ExpiresAt, string Controller, int RetentionMonths);

public sealed class DisclosureInput
{
    [Required(ErrorMessage = "Bitte die Zahl der einziehenden Personen angeben.")]
    [Range(1, 20, ErrorMessage = "Bitte eine Zahl zwischen 1 und 20 angeben.")]
    public int? Persons { get; set; }

    [StringLength(100, ErrorMessage = "Höchstens 100 Zeichen.")]
    public string? Occupation { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Bitte einen Betrag in Euro angeben.")]
    public decimal? NetIncome { get; set; }

    [StringLength(200, ErrorMessage = "Höchstens 200 Zeichen.")]
    public string? Pets { get; set; }

    public DateOnly? MoveInOn { get; set; }

    [StringLength(1000, ErrorMessage = "Höchstens 1000 Zeichen.")]
    public string? Message { get; set; }
}
