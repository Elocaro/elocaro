using System.Globalization;
using MudBlazor;

namespace MietHahn.Web.Components.Controls;

/// <summary>
/// Reads typed numbers the way people write them: 1.234,56, 1234,56, 0,090527 and also 0.090527 or 12.5 (point as decimal sign).
/// A single point followed by exactly three digits after a non-zero integer part (1.000) counts as thousands separator, as usual in German.
/// An unusable text is a conversion error, never silently dropped.
/// </summary>
public sealed class LenientNumberConverter<T> : IReversibleConverter<T?, string>
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Type Target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    public string Convert(T? input) => input is null ? "" : System.Convert.ToString(input, German) ?? "";

    public T? ConvertBack(string input)
    {
        var text = NumberText.Normalize(input);
        if (text is null)
        {
            return default;
        }

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new FormatException("Bitte eine Zahl eingeben, z. B. 1.234,56.");
        }

        if (Target == typeof(int) || Target == typeof(long) || Target == typeof(short))
        {
            if (number != decimal.Truncate(number))
            {
                throw new FormatException("Bitte eine ganze Zahl eingeben.");
            }
        }

        return (T)System.Convert.ChangeType(number, Target, CultureInfo.InvariantCulture);
    }
}

/// <summary>Normalizes typed number text for <see cref="LenientNumberConverter{T}"/>.</summary>
public static class NumberText
{
    /// <summary>Invariant text (point as decimal sign) of the typed number; null when empty.</summary>
    public static string? Normalize(string? input)
    {
        var text = input?.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lastComma = text.LastIndexOf(',');
        var lastPoint = text.LastIndexOf('.');
        if (lastComma >= 0 && lastPoint >= 0)
        {
            // The sign that comes last is the decimal sign; the other one separates thousands.
            return lastComma > lastPoint
                ? text.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
                : text.Replace(",", "", StringComparison.Ordinal);
        }

        if (lastComma >= 0)
        {
            return text.Count(c => c == ',') == 1 ? text.Replace(',', '.') : text.Replace(",", "", StringComparison.Ordinal);
        }

        if (lastPoint >= 0)
        {
            var points = text.Count(c => c == '.');
            var digitsAfter = text.Length - lastPoint - 1;
            var integerPart = text[..lastPoint].TrimStart('-', '+');
            var thousands = points > 1 || (digitsAfter == 3 && integerPart.Length is > 0 and <= 3 && integerPart != "0");
            return thousands ? text.Replace(".", "", StringComparison.Ordinal) : text;
        }

        return text;
    }
}
