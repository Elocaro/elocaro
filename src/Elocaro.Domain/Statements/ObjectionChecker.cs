using Elocaro.Domain.Common;
using Elocaro.Domain.OperatingCosts;

namespace Elocaro.Domain.Statements;

/// <summary>
/// Hints on what tenants typically object to, shown to the landlord before the release. All findings are notes
/// (<see cref="FindingSeverity.Info"/>), never a reason to block the release, and are worded as things to check, not as legal statements.
/// Jumps against the previous year and cost types that are not agreed in a lease are already covered by <see cref="PlausibilityChecker"/>.
/// </summary>
public static class ObjectionChecker
{
    private const int ShownDescriptions = 3;

    /// <param name="documented">Ids of the cost items that have a receipt with at least one file.</param>
    public static IReadOnlyList<PlausibilityFinding> Check(BillingResult result, IReadOnlySet<Guid> documented)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(documented);
        var findings = new List<PlausibilityFinding>();

        foreach (var type in result.CostTypes.Where(t => t.Total > 0 && t.Items.Count > 0))
        {
            var open = type.Items.Where(i => !documented.Contains(i.Id)).ToList();
            if (open.Count == 0)
            {
                continue;
            }

            var sum = decimal.Round(open.Sum(i => i.Prorated), 2, MidpointRounding.AwayFromZero);
            if (open.Count == type.Items.Count)
            {
                findings.Add(new PlausibilityFinding(
                    WizardStep.Costs,
                    FindingSeverity.Info,
                    $"Prüfhinweis Kostenart {type.Name}: Keine Belegdatei für {Positions(open.Count)} ({GermanText.Money(sum)}). Bei der Belegeinsicht können Mieter einen Nachweis verlangen.",
                    type.CostType));
                continue;
            }

            var shown = string.Join("; ", open.Take(ShownDescriptions).Select(i => i.Description));
            findings.Add(new PlausibilityFinding(
                WizardStep.Costs,
                FindingSeverity.Info,
                $"Prüfhinweis Kostenart {type.Name}: {open.Count} von {type.Items.Count} Positionen ({GermanText.Money(sum)}) ohne Belegdatei: {shown}{(open.Count > ShownDescriptions ? " …" : "")}.",
                type.CostType));
        }

        if (result.NonAllocable.Count > 0)
        {
            var sum = result.NonAllocable.Sum(i => i.Amount);
            findings.Add(new PlausibilityFinding(
                WizardStep.Costs,
                FindingSeverity.Info,
                $"Prüfhinweis: {Positions(result.NonAllocable.Count)} ({GermanText.Money(sum)}) sind als nicht umlagefähig markiert und fehlen in der Abrechnung. Mieter fragen oft nach Rechnungen, die sie kennen, aber nicht in der Abrechnung finden; ein kurzer Hinweis im Anschreiben kann Rückfragen vermeiden."));
        }

        return findings;
    }

    private static string Positions(int count) => count == 1 ? "1 Position" : $"{count} Positionen";
}
