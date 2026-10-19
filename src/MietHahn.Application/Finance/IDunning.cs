using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;
using MietHahn.Domain.Finance;

namespace MietHahn.Application.Finance;

/// <summary>
/// Semi-automatic dunning: arrears are detected and a letter is <b>proposed</b>; it is created and sent only
/// after approval (<see cref="IssueAsync"/>). The tenancy is the unit of dunning (one tenant account per lease).
/// </summary>
public interface IDunning
{
    /// <summary>
    /// Compares the arrears of all tenancies (or one) with the stored proposals: creates, adjusts and removes proposals.
    /// A payment since the proposal thus removes it. Returns the number of changes. Sends nothing.
    /// </summary>
    Task<int> RefreshAsync(Guid? leaseId = null, CancellationToken cancellationToken = default);

    /// <summary>Current proposals (refreshed first), issued dunnings and active holds.</summary>
    Task<DunningOverview> OverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>Dunnings and holds of one tenancy (for the tenant account).</summary>
    Task<LeaseDunning> ForLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default);

    /// <summary>Proposal with claims, recipients and preselection; <c>null</c> if it does not exist (any more).</summary>
    Task<DunningDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="GetAsync"/>, but without revalidating the proposal. For processing a submitted form: the actual
    /// decision (<see cref="IssueAsync"/>) always checks the arrears itself.
    /// </summary>
    Task<DunningDetails?> PeekAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Renders the letter as a PDF without storing or sending anything.</summary>
    Task<byte[]> PreviewAsync(Guid id, DunningIssueInput options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approval: checks the arrears again, creates and stores the letter (file store and document library, level and date are saved),
    /// books fee and interest if included and sends the email if chosen.
    /// </summary>
    Task<Result> IssueAsync(Guid id, DunningIssueInput options, CancellationToken cancellationToken = default);

    Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> AddHoldAsync(Guid leaseId, DunningHoldInput input, CancellationToken cancellationToken = default);

    Task<Result> EndHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
}

public sealed class DunningIssueInput
{
    /// <summary>Include the dunning fee in the letter and book it as a charge.</summary>
    public bool IncludeFee { get; set; } = true;

    /// <summary>Include the default interest in the letter and book it as a charge.</summary>
    public bool IncludeInterest { get; set; } = true;

    /// <summary>Also send the letter by email to the tenants (otherwise download for postal delivery).</summary>
    public bool SendByMail { get; set; }
}

public sealed class DunningHoldInput
{
    [Required(ErrorMessage = "Bitte einen Grund angeben, z. B. Ratenvereinbarung.")]
    [StringLength(500)]
    public string Reason { get; set; } = "";

    /// <summary>Last day of the hold; empty = until cancelled.</summary>
    public DateOnly? Until { get; set; }
}

public sealed record DunningRow(
    Guid Id,
    Guid LeaseId,
    string Tenant,
    string RentalUnit,
    DunningLevel Level,
    DunningStatus Status,
    DateOnly Date,
    decimal Principal,
    decimal Fee,
    decimal Interest,
    DateOnly OldestDue,
    DateOnly? Deadline,
    Guid? FileId,
    bool SentByMail,
    bool TerminationRelevant);

public sealed record DunningHoldRow(Guid Id, Guid LeaseId, string Tenant, string RentalUnit, string Reason, DateOnly? Until);

/// <summary>Arrears of a tenancy that are relevant for termination without notice (hint only).</summary>
public sealed record TerminationHint(Guid LeaseId, string Tenant, string RentalUnit, decimal MonthlyRent, decimal OpenRent, TerminationRelevance Relevance);

public sealed record DunningOverview(
    IReadOnlyList<DunningRow> Proposals,
    IReadOnlyList<DunningRow> Issued,
    IReadOnlyList<DunningHoldRow> Holds,
    IReadOnlyList<TerminationHint> TerminationHints);

public sealed record LeaseDunning(
    DunningRow? Proposal,
    IReadOnlyList<DunningRow> Issued,
    IReadOnlyList<DunningHoldRow> Holds,
    TerminationHint? TerminationHint);

public sealed record DunningClaimRow(string Text, DateOnly Due, decimal Amount, decimal Open);

/// <param name="Recipients">Email addresses of the tenants.</param>
/// <param name="Address">Lines of the postal address (address field of the letter).</param>
/// <param name="InterestRate">Annual default interest rate in percent; <c>null</c> if no interest is calculated.</param>
public sealed record DunningDetails(
    DunningRow Row,
    IReadOnlyList<DunningClaimRow> Claims,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<string> Address,
    DateOnly Deadline,
    decimal? InterestRate,
    TerminationHint? TerminationHint,
    bool InterestEnabled,
    bool LandlordProfileMissing);
