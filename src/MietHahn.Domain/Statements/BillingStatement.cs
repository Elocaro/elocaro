using MietHahn.Domain.Common;

namespace MietHahn.Domain.Statements;

/// <summary>
/// A released operating cost statement of a property for a billing period. The calculation result is frozen as a
/// snapshot on release; a statement can never be changed. A correction creates a new <see cref="Revision"/> that
/// refers to the replaced one (<see cref="SupersedesId"/>).
/// </summary>
public sealed class BillingStatement : Entity
{
    private BillingStatement()
    {
        Snapshot = "";
    }

    public BillingStatement(
        Guid propertyId,
        Period billingPeriod,
        int revision,
        Guid? supersedesId,
        string snapshot,
        decimal totalCosts,
        decimal landlordTotal,
        DateTimeOffset releasedAt,
        string? note)
    {
        if (billingPeriod.To is not { } end || billingPeriod.From.AddYears(1).AddDays(-1) < end)
        {
            throw new RuleViolationException("Der Abrechnungszeitraum muss abgeschlossen sein und darf höchstens 12 Monate umfassen.");
        }

        if (revision < 1 || (revision == 1) != (supersedesId is null))
        {
            throw new RuleViolationException("Nur die erste Fassung einer Abrechnung hat keinen Vorgänger.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        PropertyId = propertyId;
        BillingPeriod = billingPeriod;
        Revision = revision;
        SupersedesId = supersedesId;
        Snapshot = snapshot;
        TotalCosts = totalCosts;
        LandlordTotal = landlordTotal;
        ReleasedAt = releasedAt;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid PropertyId { get; private set; }

    public Period BillingPeriod { get; private set; }

    /// <summary>Number of the version of this period, starting at 1.</summary>
    public int Revision { get; private set; }

    /// <summary>The revision that this one replaces; <c>null</c> for the first.</summary>
    public Guid? SupersedesId { get; private set; }

    /// <summary>Frozen calculation result (JSON of the billing result).</summary>
    public string Snapshot { get; private set; }

    public decimal TotalCosts { get; private set; }

    public decimal LandlordTotal { get; private set; }

    public DateTimeOffset ReleasedAt { get; private set; }

    /// <summary>Reason of a correction or remark on the release.</summary>
    public string? Note { get; private set; }
}
