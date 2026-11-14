using Elocaro.Application.Leases;
using Elocaro.Domain.Leases;
using Elocaro.Web.Components.Controls;
using Elocaro.Web.Display;

namespace Elocaro.Web.Components.Pages.Admin.Leases;

/// <summary>What <c>RentIncreasePage</c> decides without a browser: which kinds the lease allows, the process steps of a letter and the template keyword.</summary>
public static class RentIncreaseFlow
{
    /// <summary>The kind a new letter starts with, by what the lease agrees.</summary>
    public static RentIncreaseKind DefaultKind(RentModel model, BillingType billingType) => model switch
    {
        RentModel.Index => RentIncreaseKind.Index,
        RentModel.Graduated => RentIncreaseKind.Graduated,
        _ => billingType == BillingType.FlatRate ? RentIncreaseKind.FlatRate : RentIncreaseKind.Comparative,
    };

    /// <summary>What the lease allows, and whatever is selected; comparative rent and modernisation only without an agreed rent path (§§ 557a, 557b BGB).</summary>
    public static IEnumerable<RentIncreaseKind> AllowedKinds(RentModel model, BillingType billingType, RentIncreaseKind selected) =>
        Enum.GetValues<RentIncreaseKind>().Where(kind => kind == selected || kind switch
        {
            RentIncreaseKind.Index => model == RentModel.Index,
            RentIncreaseKind.Graduated => model == RentModel.Graduated,
            RentIncreaseKind.FlatRate => billingType == BillingType.FlatRate,
            RentIncreaseKind.Comparative => model == RentModel.Standard,
            _ => model != RentModel.Graduated,
        });

    /// <summary>Part of a template name that marks it for the kind.</summary>
    public static string Keyword(RentIncreaseKind kind) => kind switch
    {
        RentIncreaseKind.Index => "Indexmiete",
        RentIncreaseKind.Graduated => "Staffel",
        RentIncreaseKind.FlatRate => "Pauschale",
        RentIncreaseKind.Modernisation => "Modernisierung",
        _ => "Vergleichsmiete",
    };

    /// <summary>Process steps of a letter: created, received, consent (comparative rent only) and effective.</summary>
    public static List<ProcessStep> Steps(RentIncreaseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rejected = row.Status is not (RentIncreaseStatus.Draft or RentIncreaseStatus.Declared or RentIncreaseStatus.Agreed);
        var needsConsent = row.Kind == RentIncreaseKind.Comparative;
        var steps = new List<ProcessStep>
        {
            new("Schreiben erzeugt", ProcessState.Done),
            new("Zugegangen", row.DeclaredOn is not null ? ProcessState.Done : row.Status == RentIncreaseStatus.Draft ? ProcessState.Current : ProcessState.Open, row.DeclaredOn is { } declared ? Format.Date(declared) : null),
        };
        if (needsConsent)
        {
            steps.Add(new("Zustimmung", row.ConsentOn is not null ? ProcessState.Done : row.Status == RentIncreaseStatus.Declared ? ProcessState.Current : ProcessState.Open,
                row.ConsentOn is { } consent ? Format.Date(consent) : row.ConsentDeadline is { } deadline ? $"bis {Format.Date(deadline)}" : null));
        }

        steps.Add(rejected
            ? new(Labels.From(row.Status), ProcessState.Done)
            : new("Wirksam", row.Status == RentIncreaseStatus.Agreed || (!needsConsent && row.DeclaredOn is not null) ? ProcessState.Done : ProcessState.Open, $"ab {Format.Date(row.EffectiveFrom)}"));
        return steps;
    }
}
