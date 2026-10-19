using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace MietHahn.Web.Components.Common;

/// <summary>
/// Gemeinsame Basis für Dezimaleingaben. Gerendert wird <c>&lt;input type="number" step="0.01"&gt;</c>: Der Browser zeigt
/// und akzeptiert die Zahl in der Schreibweise des Nutzers („1234,56“), sendet sie aber immer mit Punkt. Das ist nötig,
/// weil Blazor bei statischem SSR Formularwerte kulturinvariant bindet (<c>[SupplyParameterFromForm]</c>); ein Textfeld
/// mit „30,5“ käme nicht als Zahl an. Mobil öffnet sich die Zahlentastatur.
/// </summary>
public abstract class InputDecimalBase : InputBase<decimal?>
{
    protected static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    protected abstract string ErrorText(string? input);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "step", "0.01");
        builder.AddMultipleAttributes(2, AdditionalAttributes);
        builder.AddAttribute(3, "type", "number");
        builder.AddAttribute(4, "inputmode", "decimal");
        builder.AddAttribute(5, "autocomplete", "off");
        builder.AddAttribute(6, "name", NameAttributeValue);
        builder.AddAttribute(7, "class", CssClass);
        builder.AddAttribute(8, "value", CurrentValueAsString);
        builder.AddAttribute(9, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.CloseElement();
    }

    /// <summary>Wert für das <c>value</c>-Attribut: immer mit Punkt, ohne Tausendertrennzeichen.</summary>
    protected override string? FormatValueAsString(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out decimal? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            validationErrorMessage = null;
            return true;
        }

        // Vom Browser kommt der Wert mit Punkt; deutsche Schreibweise nur als Rückfall (z. B. interaktive Eingabe).
        decimal? parsed = decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var invariant)
            ? invariant
            : InputAmount.Read(value, out var german) ? german : null;
        if (parsed is not null)
        {
            result = Math.Round(parsed.Value, 2, MidpointRounding.AwayFromZero);
            validationErrorMessage = null;
            return true;
        }

        result = null;
        validationErrorMessage = ErrorText(value);
        return false;
    }
}

/// <summary>Flächen in m² (z. B. 75,5), höchstens zwei Nachkommastellen.</summary>
public sealed class InputArea : InputDecimalBase
{
    protected override string ErrorText(string? input) => $"„{input}“ ist keine gültige Fläche (z. B. 75,5).";
}

/// <summary>Geldbeträge in Euro, auf Cent gerundet.</summary>
public sealed class InputAmount : InputDecimalBase
{
    protected override string ErrorText(string? input) => $"„{input}“ ist kein gültiger Betrag (z. B. 1234,56).";

    /// <summary>Liest einen Betrag in deutscher (oder einfacher Punkt-)Schreibweise, z. B. aus importierten Daten.</summary>
    public static bool Read(string? text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cleaned = text.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();

        // „1234.56“ (nur ein Punkt, max. 2 Nachkommastellen, kein Komma) als Dezimalpunkt verstehen.
        if (!cleaned.Contains(',', StringComparison.Ordinal) && cleaned.Count(z => z == '.') == 1 && cleaned.Length - cleaned.IndexOf('.', StringComparison.Ordinal) - 1 is 1 or 2)
        {
            cleaned = cleaned.Replace('.', ',');
        }

        if (!decimal.TryParse(cleaned, NumberStyles.Number, German, out var value))
        {
            return false;
        }

        amount = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return true;
    }
}
