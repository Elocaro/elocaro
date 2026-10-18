using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace MietHahn.Web.Components.Gemeinsam;

/// <summary>
/// Eingabe für Geldbeträge in deutscher Schreibweise: akzeptiert „1.234,56“, „1234,56“, „1234.56“ und „-12 €“,
/// rundet kaufmännisch auf Cent und zeigt „1234,56“ an. Mobil öffnet sich die Zahlentastatur (<c>inputmode=decimal</c>).
/// </summary>
public sealed class InputBetrag : InputBase<decimal?>
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "text");
        builder.AddAttribute(3, "inputmode", "decimal");
        builder.AddAttribute(4, "autocomplete", "off");
        builder.AddAttribute(5, "name", NameAttributeValue);
        builder.AddAttribute(6, "class", CssClass);
        builder.AddAttribute(7, "value", CurrentValueAsString);
        builder.AddAttribute(8, "onchange", EventCallback.Factory.CreateBinder<string?>(this, wert => CurrentValueAsString = wert, CurrentValueAsString));
        builder.CloseElement();
    }

    protected override string? FormatValueAsString(decimal? value) => value?.ToString("0.00", Deutsch);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out decimal? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (Lesen(value, out result))
        {
            validationErrorMessage = null;
            return true;
        }

        validationErrorMessage = $"„{value}“ ist kein gültiger Betrag (z. B. 1.234,56).";
        return false;
    }

    /// <summary>Liest einen Betrag in deutscher (oder einfacher Punkt-)Schreibweise.</summary>
    public static bool Lesen(string? text, out decimal? betrag)
    {
        betrag = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var bereinigt = text.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();

        // „1234.56“ (nur ein Punkt, max. 2 Nachkommastellen, kein Komma) als Dezimalpunkt verstehen.
        if (!bereinigt.Contains(',', StringComparison.Ordinal) && bereinigt.Count(z => z == '.') == 1 && bereinigt.Length - bereinigt.IndexOf('.', StringComparison.Ordinal) - 1 is 1 or 2)
        {
            bereinigt = bereinigt.Replace('.', ',');
        }

        if (!decimal.TryParse(bereinigt, NumberStyles.Number, Deutsch, out var wert))
        {
            return false;
        }

        betrag = Math.Round(wert, 2, MidpointRounding.AwayFromZero);
        return true;
    }
}
