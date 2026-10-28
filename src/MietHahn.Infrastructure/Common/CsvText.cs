using System.Globalization;
using System.Text;

namespace MietHahn.Infrastructure.Common;

/// <summary>Generic CSV handling shared by the bank, receipt and loan imports.</summary>
internal static class CsvText
{
    /// <summary>„-1.234,56“, „1234,56 €“, „+12,00“ or with a period as the decimal separator.</summary>
    internal static decimal? Amount(string? text, char decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string(text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '+').ToArray());
        cleaned = decimalSeparator == ','
            ? cleaned.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
            : cleaned.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    /// <summary>Parses CSV according to RFC 4180 (quotes, line breaks within fields).</summary>
    internal static IEnumerable<List<string>> Rows(string text, char separator)
    {
        var field = new StringBuilder();
        var line = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == separator)
            {
                line.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                line.Add(field.ToString());
                field.Clear();
                yield return line;
                line = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || line.Count > 0)
        {
            line.Add(field.ToString());
            yield return line;
        }
    }
}
