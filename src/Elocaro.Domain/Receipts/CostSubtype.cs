using Elocaro.Domain.Common;

namespace Elocaro.Domain.Receipts;

/// <summary>
/// Own subtype of a cost type of the catalog (e.g. "Gebäudeversicherung" and "Haftpflicht" under no. 13 BetrKV).
/// Allocation keys and statement lines refer to the cost type; the subtype refines receipts and the calculation protocol.
/// </summary>
public sealed class CostSubtype : Entity
{
    private CostSubtype()
    {
        CostType = "";
        Name = "";
    }

    public CostSubtype(string costType, string name)
    {
        if (!CostTypes.IsKnown(costType))
        {
            throw new RuleViolationException("Unbekannte Kostenart.");
        }

        Name = Normalize(name);
        CostType = costType;
    }

    /// <summary>Key of the parent cost type in <see cref="CostTypes"/>.</summary>
    public string CostType { get; private set; }

    public string Name { get; private set; }

    public void Rename(string name) => Name = Normalize(name);

    private static string Normalize(string name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length is 0 or > 100)
        {
            throw new RuleViolationException("Bitte einen Namen mit höchstens 100 Zeichen angeben.");
        }

        return value;
    }
}
