using System.Globalization;
using Elocaro.Domain.OperatingCosts;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Statements;

/// <summary>The steps of the statement wizard.</summary>
public enum WizardStep
{
    PeriodAndProperty = 1,
    Costs = 2,
    Meters = 3,
    Occupancy = 4,
    Calculation = 5,
    Release = 6,
}

public enum FindingSeverity
{
    Info = 0,

    /// <summary>Can be released after acknowledgement.</summary>
    Warning = 1,

    /// <summary>The statement must not be released.</summary>
    Error = 2,
}

public sealed record PlausibilityFinding(WizardStep Step, FindingSeverity Severity, string Text, string? CostType = null);

/// <summary>
/// Plausibility checks on a calculation result: comparison with the previous year, recurring cost types, allocation key sums,
/// cost types not agreed in the lease and prepayments against target. Checks that need data outside the result
/// (meters, occupancy) are made by the application service.
/// </summary>
public static class PlausibilityChecker
{
    public const decimal DefaultThresholdPercent = 20m;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static IReadOnlyList<PlausibilityFinding> Check(BillingResult current, BillingResult? previous, decimal thresholdPercent = DefaultThresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(current);
        var findings = new List<PlausibilityFinding>();

        foreach (var problem in current.Problems)
        {
            findings.Add(new PlausibilityFinding(
                WizardStep.Calculation,
                problem.Severity == ProblemSeverity.Error ? FindingSeverity.Error : FindingSeverity.Warning,
                problem.Text,
                problem.CostType));
        }

        if (previous is not null)
        {
            CompareWithPrevious(current, previous, thresholdPercent, findings);
        }

        foreach (var type in current.CostTypes)
        {
            if (type.TenantTotal + type.LandlordTotal != type.Total)
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Calculation,
                    FindingSeverity.Error,
                    $"Kostenart {type.Name}: Die Anteile ({Money(type.TenantTotal)} Mieter, {Money(type.LandlordTotal)} Vermieter) ergeben nicht die Gesamtkosten {Money(type.Total)}.",
                    type.CostType));
            }
        }

        foreach (var tenant in current.Tenants)
        {
            foreach (var line in tenant.Lines.Where(l => l.NotAgreed > 0))
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Calculation,
                    FindingSeverity.Warning,
                    $"{tenant.Label}: Die Kostenart {line.Name} ist im Mietvertrag nicht vereinbart; {Money(line.NotAgreed)} bleiben beim Vermieter.",
                    line.CostType));
            }

            foreach (var group in tenant.Groups.Where(g => g.Calculated && g.PrepaymentsDue != g.PrepaymentsPaid))
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Calculation,
                    FindingSeverity.Warning,
                    $"{tenant.Label}: Vorauszahlungen {(group.Group == CostGroup.Heating ? "Heizkosten" : "Betriebskosten")} gezahlt {Money(group.PrepaymentsPaid)} statt Soll {Money(group.PrepaymentsDue)}."));
            }
        }

        return findings;
    }

    private static void CompareWithPrevious(BillingResult current, BillingResult previous, decimal thresholdPercent, List<PlausibilityFinding> findings)
    {
        var before = previous.CostTypes.ToDictionary(t => t.CostType);
        var now = current.CostTypes.ToDictionary(t => t.CostType);

        foreach (var (key, old) in before.Where(p => p.Value.Total > 0))
        {
            if (!now.TryGetValue(key, out var type) || type.Total == 0)
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Costs,
                    FindingSeverity.Warning,
                    $"Kostenart {old.Name}: Im Vorjahr {Money(old.Total)}, in diesem Zeitraum keine Kosten erfasst. Fehlt ein Beleg?",
                    key));
                continue;
            }

            var change = (type.Total - old.Total) / old.Total * 100m;
            if (Math.Abs(change) > thresholdPercent)
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Costs,
                    FindingSeverity.Warning,
                    $"Kostenart {old.Name}: {Money(type.Total)} gegenüber {Money(old.Total)} im Vorjahr ({(change > 0 ? "+" : "")}{change.ToString("0.#", German)} %, Grenze ±{thresholdPercent.ToString("0.#", German)} %).",
                    key));
            }
        }

        foreach (var type in current.CostTypes.Where(t => t.Total > 0 && !before.ContainsKey(t.CostType)))
        {
            findings.Add(new PlausibilityFinding(
                WizardStep.Costs, FindingSeverity.Info, $"Kostenart {type.Name} ist neu gegenüber dem Vorjahr ({Money(type.Total)}).", type.CostType));
        }
    }

    private static string Money(decimal value) => value.ToString("N2", German) + " €";
}
