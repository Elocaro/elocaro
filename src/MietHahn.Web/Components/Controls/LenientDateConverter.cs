using System.Globalization;
using MudBlazor;

namespace MietHahn.Web.Components.Controls;

/// <summary>
/// Reads typed dates the way people write them: 01.11.2026, 1.11.26, 01112026 or 2026-11-01 (months: 11.2026, 11/2026, 112026, 2026-11).
/// An unusable text is a conversion error (shown below the field), never silently dropped.
/// </summary>
public sealed class LenientDateConverter(bool monthOnly = false) : IReversibleConverter<DateTime?, string>
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly string[] DayFormats = ["d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "ddMMyyyy", "ddMMyy", "yyyy-MM-dd", "d/M/yyyy"];
    private static readonly string[] MonthFormats = ["M.yyyy", "MM.yyyy", "MMyyyy", "M/yyyy", "yyyy-MM", "M.yy"];

    public static LenientDateConverter Day { get; } = new();

    public static LenientDateConverter Month { get; } = new(monthOnly: true);

    /// <summary>True when the last text that was read could not be understood (the field clears it, so the owner shows the error).</summary>
    public bool LastReadFailed { get; private set; }

    /// <summary>The date format shown in the field and used in the hint.</summary>
    public string Format => monthOnly ? "MM.yyyy" : "dd.MM.yyyy";

    public string Convert(DateTime? input) => input?.ToString(Format, Invariant) ?? "";

    public DateTime? ConvertBack(string input)
    {
        var date = Read(input, monthOnly);
        LastReadFailed = !string.IsNullOrWhiteSpace(input) && date is null;
        if (LastReadFailed)
        {
            throw new FormatException(monthOnly ? "Bitte einen Monat wie 11.2026 eingeben." : "Bitte ein Datum wie 01.11.2026 eingeben.");
        }

        return date;
    }

    /// <summary>Parses the text; null when it is empty or not a valid date.</summary>
    public static DateTime? Read(string? text, bool monthOnly = false)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // A month field also accepts a full date (the day is ignored by the owner).
        var formats = monthOnly ? [.. MonthFormats, .. DayFormats] : DayFormats;
        if (DateTime.TryParseExact(value, formats, Invariant, DateTimeStyles.None, out var parsed))
        {
            // Two-digit years: 26 means 2026 (never 1926); the .NET default pivot would make 70 and above 19xx.
            return parsed.Year < 100 ? parsed.AddYears(2000) : parsed;
        }

        return null;
    }
}
