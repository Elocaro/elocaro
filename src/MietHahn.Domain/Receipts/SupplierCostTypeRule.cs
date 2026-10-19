using System.Globalization;
using System.Text;
using MietHahn.Domain.Common;

namespace MietHahn.Domain.Receipts;

/// <summary>
/// Learned rule "supplier → cost type": whenever a receipt is booked to a cost type, the supplier remembers it and the
/// next extraction proposes the same cost type. Changes are recorded in the change log like all entities.
/// </summary>
public sealed class SupplierCostTypeRule : Entity
{
    private SupplierCostTypeRule()
    {
        SupplierKey = "";
        CostType = "";
    }

    public SupplierCostTypeRule(string supplierKey, string costType)
    {
        if (string.IsNullOrWhiteSpace(supplierKey))
        {
            throw new RuleViolationException("Der Lieferant fehlt.");
        }

        SupplierKey = supplierKey;
        CostType = "";
        Learn(costType);
    }

    /// <summary>See <see cref="KeyFor"/>.</summary>
    public string SupplierKey { get; private set; }

    /// <summary>Key from <see cref="CostTypes"/>.</summary>
    public string CostType { get; private set; }

    /// <summary>How often the rule has been confirmed by saving a receipt.</summary>
    public int Uses { get; private set; }

    public void Learn(string costType)
    {
        if (!CostTypes.IsKnown(costType))
        {
            throw new RuleViolationException("Unbekannte Kostenart.");
        }

        Uses = CostType == costType ? Uses + 1 : 1;
        CostType = costType;
    }

    /// <summary>Contact → <c>contact:{id}</c>; otherwise the normalized name → <c>name:…</c>; <c>null</c> without a supplier.</summary>
    public static string? KeyFor(Guid? supplierId, string? name) =>
        supplierId is { } id ? "contact:" + id.ToString("N") : NameKey(name);

    public static string? NameKey(string? name)
    {
        var normalized = Normalize(name);
        return normalized.Length == 0 ? null : "name:" + normalized;
    }

    /// <summary>Lowercase, umlauts kept, everything but letters and digits becomes a single space ("Müller &amp; Söhne GmbH" → "müller söhne gmbh").</summary>
    public static string Normalize(string? name)
    {
        var text = new StringBuilder();
        foreach (var c in (name ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                text.Append(c);
            }
            else if (text.Length > 0 && text[^1] != ' ')
            {
                text.Append(' ');
            }
        }

        return text.ToString().Trim().Normalize(NormalizationForm.FormC).Replace("ß", "ss", false, CultureInfo.InvariantCulture);
    }
}
