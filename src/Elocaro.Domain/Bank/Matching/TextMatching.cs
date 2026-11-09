using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Elocaro.Domain.Bank.Matching;

/// <summary>Text helpers of the matching engine: normalization, fuzzy tokens and month patterns in purposes.</summary>
public static partial class TextMatching
{
    private static readonly (string[] Names, int Month)[] MonthNames =
    [
        (["januar", "jan"], 1),
        (["februar", "feb"], 2),
        (["maerz", "maer", "mrz"], 3),
        (["april", "apr"], 4),
        (["mai"], 5),
        (["juni", "jun"], 6),
        (["juli", "jul"], 7),
        (["august", "aug"], 8),
        (["september", "sept", "sep"], 9),
        (["oktober", "okt"], 10),
        (["november", "nov"], 11),
        (["dezember", "dez"], 12),
    ];

    /// <summary>Lowercase, umlauts transliterated, everything except letters and digits becomes a single space.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var lower = text.ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal);
        var builder = new StringBuilder(lower.Length);
        var lastSpace = true;
        foreach (var c in lower.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                builder.Append(' ');
                lastSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static IReadOnlyList<string> Tokens(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Equal, or (for tokens of at least 6 letters) one typo apart.</summary>
    public static bool TokenMatches(string a, string b) =>
        a == b || (Math.Min(a.Length, b.Length) >= 6 && Math.Abs(a.Length - b.Length) <= 1 && Distance(a, b) <= 1);

    /// <summary>Whether the normalized phrase appears in the normalized text as whole words.</summary>
    public static bool ContainsPhrase(string? text, string? phrase)
    {
        var haystack = Normalize(text);
        var needle = Normalize(phrase);
        return needle.Length > 0 && $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Like <see cref="ContainsPhrase"/> but unit names are compared with common abbreviations resolved
    /// ("Whg." / "WE" = "Wohnung"), e.g. "Wohnung 3" in "Miete Whg 3".
    /// </summary>
    public static bool ContainsUnit(string? text, string? unitName)
    {
        var haystack = Canonical(text);
        var needle = Canonical(unitName);
        return needle.Length > 0 && $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Months (first of the month) named in the purpose: "03/26", "03.2026", "2026-03", "März 2026", "Jan-Mär 2026",
    /// "01-03/2026". Complete dates such as "01.03.2026" are not months. Without a year, the year of the booking is assumed.
    /// </summary>
    public static IReadOnlyList<DateOnly> Months(string? purpose, DateOnly booking)
    {
        var result = new SortedSet<DateOnly>();
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return [];
        }

        foreach (Match match in RangeNumeric().Matches(purpose))
        {
            var from = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var to = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = Year(match.Groups[3].Value);
            if (from <= to)
            {
                for (var month = from; month <= to; month++)
                {
                    result.Add(new DateOnly(year, month, 1));
                }
            }
        }

        foreach (Match match in SingleNumeric().Matches(purpose))
        {
            result.Add(new DateOnly(Year(match.Groups[2].Value), int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 1));
        }

        foreach (Match match in IsoMonth().Matches(purpose))
        {
            result.Add(new DateOnly(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 1));
        }

        AddNamedMonths(Tokens(purpose), booking, result);
        return result.ToList();
    }

    private static void AddNamedMonths(IReadOnlyList<string> tokens, DateOnly booking, SortedSet<DateOnly> result)
    {
        var i = 0;
        while (i < tokens.Count)
        {
            if (MonthOf(tokens[i]) is not { } first)
            {
                i++;
                continue;
            }

            // A group: months separated by "bis", "und", "u" or directly next to each other.
            var months = new List<int> { first };
            var connector = "";
            var j = i + 1;
            while (j < tokens.Count)
            {
                var token = tokens[j];
                if (token is "bis" or "und" or "u")
                {
                    connector = token;
                    j++;
                    continue;
                }

                if (MonthOf(token) is not { } next)
                {
                    break;
                }

                // "Jan bis Mär" and "Jan-Mär" cover the months in between, "Jan und Mär" does not.
                if (connector != "und" && connector != "u")
                {
                    for (var month = months[^1] + 1; month < next; month++)
                    {
                        months.Add(month);
                    }
                }

                months.Add(next);
                connector = "";
                j++;
            }

            int? year = null;
            if (j < tokens.Count && IsYear(tokens[j]))
            {
                year = Year(tokens[j]);
                j++;
            }
            else if (i > 0 && IsYear(tokens[i - 1]) && tokens[i - 1].Length == 4)
            {
                year = Year(tokens[i - 1]);
            }

            foreach (var month in months)
            {
                // Without a year: December paid in January belongs to the previous year.
                var y = year ?? (month > booking.Month + 1 ? booking.Year - 1 : booking.Year);
                result.Add(new DateOnly(y, month, 1));
            }

            i = Math.Max(j, i + 1);
        }
    }

    private static int? MonthOf(string token)
    {
        foreach (var (names, month) in MonthNames)
        {
            if (names.Contains(token))
            {
                return month;
            }
        }

        return null;
    }

    private static bool IsYear(string token) =>
        token.All(char.IsAsciiDigit) && (token.Length == 4 && token.StartsWith("20", StringComparison.Ordinal));

    private static int Year(string text)
    {
        var year = int.Parse(text, CultureInfo.InvariantCulture);
        return year < 100 ? 2000 + year : year;
    }

    private static string Canonical(string? text) =>
        string.Join(' ', Tokens(text).Select(t => t is "whg" or "we" or "wohn" or "wohneinheit" or "appartement" or "apartment" or "app" ? "wohnung" : t));

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    [GeneratedRegex(@"(?<![\d.,/])(0?[1-9]|1[0-2])\s*-\s*(0?[1-9]|1[0-2])\s*[/.]\s*(20\d{2}|\d{2})(?!\d|\.\d)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RangeNumeric();

    [GeneratedRegex(@"(?<![\d.,/-])(0?[1-9]|1[0-2])\s*[/.]\s*(20\d{2}|\d{2})(?!\d|\.\d|/\d)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SingleNumeric();

    [GeneratedRegex(@"(?<!\d)(20\d{2})[-/](0[1-9]|1[0-2])(?!\d|-\d)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IsoMonth();
}
