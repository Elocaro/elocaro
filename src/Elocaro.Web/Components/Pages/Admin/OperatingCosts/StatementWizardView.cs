using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Elocaro.Application.Statements;
using Elocaro.Domain.Common;
using Elocaro.Domain.Statements;
using Elocaro.Web.Components.Common;
using Elocaro.Web.Display;

namespace Elocaro.Web.Components.Pages.Admin.OperatingCosts;

/// <summary>One line of the setup list of the statement wizard (what the statement relies on besides receipts and meters).</summary>
public sealed record WizardSetupItem(string Title, string Status, StatusLevel Level, string Text, string Href);

/// <summary>One line of the cost checklist: this year against the previous one.</summary>
public sealed record WizardCostRow(string Name, decimal Current, decimal? Previous, string Change, string Status, bool Missing);

/// <summary>Form of the last wizard step.</summary>
public sealed class ReleaseInput
{
    public bool Acknowledge { get; set; }

    [StringLength(2000)]
    public string? Note { get; set; }
}

/// <summary>What the statement wizard shows and derives from the prepared <see cref="StatementWizard"/> without a browser.</summary>
public static class StatementWizardView
{
    /// <summary>The six steps; findings that need attention show as a badge in the step list.</summary>
    public static IReadOnlyList<WizardStepInfo> StepsOf(StatementWizard wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        WizardStepInfo Step(WizardStep target, string title, string? description = null)
        {
            var count = wizard.Findings.Count(f => f.Step == target && f.Severity != FindingSeverity.Info);
            return new(title, Description: description, Badge: count > 0 ? count.ToString(CultureInfo.InvariantCulture) : null,
                BadgeLabel: count > 0 ? (count == 1 ? "1 Hinweis" : $"{count} Hinweise") : null);
        }

        var period = Format.Period(wizard.Preview.Result.BillingPeriod);
        return
        [
            Step(WizardStep.PeriodAndProperty, "Zeitraum und Objekt"),
            Step(WizardStep.Costs, "Checkliste Kosten", $"Erfasste Kostenarten {period} im Vergleich zum Vorjahr. Fehlende wiederkehrende Kostenarten sind markiert."),
            Step(WizardStep.Meters, "Zählerstände vollständig?"),
            Step(WizardStep.Occupancy, "Personen und Belegung prüfen"),
            Step(WizardStep.Calculation, "Berechnung und Vorschau"),
            Step(WizardStep.Release, "Freigabe"),
        ];
    }

    /// <summary>Number of months of a billing period (at least one).</summary>
    public static decimal MonthsOf(Period period)
    {
        return Math.Max(1, (period.To!.Value.Year - period.From.Year) * 12 + period.To.Value.Month - period.From.Month + 1);
    }

    /// <summary>Cost types of this and the previous year side by side, with the change and a status ("ok", "neu", "prüfen", "fehlt").</summary>
    public static IEnumerable<WizardCostRow> CostRows(StatementWizard wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        var current = wizard.Preview.Result.CostTypes.ToDictionary(t => t.CostType);
        var previous = wizard.Previous?.CostTypes.ToDictionary(t => t.CostType) ?? [];
        foreach (var key in current.Keys.Union(previous.Keys).OrderBy(k => (current.GetValueOrDefault(k) ?? previous[k]).Name, StringComparer.CurrentCulture))
        {
            var now = current.GetValueOrDefault(key);
            var old = previous.GetValueOrDefault(key);
            var change = now is not null && old is { Total: > 0 } ? (now.Total - old.Total) / old.Total * 100m : (decimal?)null;
            var finding = wizard.Findings.Any(f => f.Step == WizardStep.Costs && f.CostType == key && f.Severity == FindingSeverity.Warning);
            yield return new WizardCostRow(
                (now ?? old)!.Name,
                now?.Total ?? 0m,
                old?.Total,
                change is { } value ? $"{(value > 0 ? "↑ +" : value < 0 ? "↓ " : "")}{value:0.#} %" : "–",
                now is null || now.Total == 0 ? "fehlt" : old is null ? "neu" : finding ? "prüfen" : "ok",
                (now is null || now.Total == 0) && old is { Total: > 0 });
        }
    }
}
