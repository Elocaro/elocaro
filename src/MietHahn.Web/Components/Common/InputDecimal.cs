using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace MietHahn.Web.Components.Common;

/// <summary>
/// Common base for decimal inputs. Renders <c>&lt;input type="number" step="0.01"&gt;</c>: The browser shows
/// and accepts the number in the user's notation ("1234,56") but always submits it with a decimal point. This is necessary
/// because Blazor binds form values culture-invariantly in static SSR (<c>[SupplyParameterFromForm]</c>); a text field
/// with "30,5" would not arrive as a number. On mobile the numeric keypad opens.
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

    /// <summary>Value for the <c>value</c> attribute: always with a decimal point, without thousands separators.</summary>
    protected override string? FormatValueAsString(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out decimal? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            validationErrorMessage = null;
            return true;
        }

        // The browser sends the value with a decimal point; German notation only as a fallback (e.g. interactive input).
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

/// <summary>Areas in m² (e.g. 75.5), at most two decimal places.</summary>
public sealed class InputArea : InputDecimalBase
{
    protected override string ErrorText(string? input) => $"„{input}“ ist keine gültige Fläche (z. B. 75,5).";
}

/// <summary>Amounts in euros, rounded to cents.</summary>
public sealed class InputAmount : InputDecimalBase
{
    protected override string ErrorText(string? input) => $"„{input}“ ist kein gültiger Betrag (z. B. 1234,56).";

    /// <summary>Reads an amount in German (or plain decimal-point) notation, e.g. from imported data.</summary>
    public static bool Read(string? text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cleaned = text.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();

        // Treat "1234.56" (a single dot, max. 2 decimal places, no comma) as a decimal point.
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
