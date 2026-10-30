using Elocaro.Application.Common;
using Elocaro.Application.OperatingCosts;
using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Statements;

namespace Elocaro.Application.Statements;

/// <summary>Statement wizard: checks the data basis, releases (freezes) a statement and manages its revisions.</summary>
public interface IStatements
{
    /// <summary>Calculates and checks the period for the wizard; <c>null</c> if the property does not exist. Saves nothing.</summary>
    Task<StatementWizard?> PrepareAsync(Guid propertyId, Period billingPeriod, decimal thresholdPercent = PlausibilityChecker.DefaultThresholdPercent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Freezes the current calculation as a released statement. An existing statement of the period is replaced by a new revision
    /// (a <paramref name="note"/> with the reason is then required). Warnings must be acknowledged, errors prevent the release.
    /// </summary>
    Task<Result> ReleaseAsync(Guid propertyId, Period billingPeriod, bool acknowledgeWarnings, string? note, CancellationToken cancellationToken = default);

    /// <summary>All released statements of the property, newest period and revision first.</summary>
    Task<IReadOnlyList<StatementSummary>> ListAsync(Guid propertyId, CancellationToken cancellationToken = default);

    Task<StoredStatement?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Hides (or shows again) the reminder for the statement of a calendar year on the dashboard and in the deadline list.</summary>
    Task SetReminderDismissedAsync(Guid propertyId, int year, bool dismissed, CancellationToken cancellationToken = default);
}

public sealed record StatementSummary(Guid Id, Guid PropertyId, Period BillingPeriod, int Revision, DateTimeOffset ReleasedAt, string? ReleasedBy, decimal TotalCosts, decimal LandlordTotal, string? Note, bool Current);

/// <param name="Difference">Difference to the replaced revision; <c>null</c> for the first.</param>
/// <param name="ClaimsExcluded">The correction raises claims after the deadline (§ 556 para. 3 BGB).</param>
public sealed record StoredStatement(StatementSummary Summary, string PropertyName, BillingResult Result, StatementDifference? Difference, bool ClaimsExcluded);

/// <param name="Previous">Result of the previous year (comparison basis), <c>null</c> if not calculable.</param>
/// <param name="Released">Newest released revision of the period.</param>
/// <param name="Deadline">State of the deadline under § 556 para. 3 BGB, <c>null</c> as long as no reminder is due.</param>
/// <param name="ReminderDismissed">The reminder for this year is hidden (dashboard, deadline list).</param>
public sealed record StatementWizard(
    OperatingCostPreview Preview,
    BillingResult? Previous,
    IReadOnlyList<PlausibilityFinding> Findings,
    DateOnly DeadlineDate,
    DeadlineState? Deadline,
    StatementSummary? Released,
    decimal ThresholdPercent,
    bool ReminderDismissed = false)
{
    public Period BillingPeriod => Preview.Result.BillingPeriod;

    public bool HasErrors => Findings.Any(f => f.Severity == FindingSeverity.Error);

    public bool HasWarnings => Findings.Any(f => f.Severity == FindingSeverity.Warning);

    public IEnumerable<PlausibilityFinding> For(WizardStep step) => Findings.Where(f => f.Step == step).OrderByDescending(f => f.Severity);
}
