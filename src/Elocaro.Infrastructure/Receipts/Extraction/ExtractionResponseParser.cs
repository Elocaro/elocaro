using System.Globalization;
using System.Text.Json;
using Elocaro.Application.Receipts;
using Elocaro.Domain.Receipts;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>
/// Turns the model output into a <see cref="ReceiptSuggestion"/>. Tolerant towards the usual deviations (code fences,
/// text around the JSON, amounts and dates as text in German notation, missing confidence), strict about structure:
/// anything that is not a JSON object raises an <see cref="ExtractionException"/>.
/// </summary>
internal static class ExtractionResponseParser
{
    private const double MissingConfidence = 0.5;
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK"];

    public static ReceiptSuggestion Parse(string? modelOutput)
    {
        var json = ExtractJson(modelOutput) ?? throw Unreadable();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return Parse(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new ExtractionException("Die Antwort des Modells war kein gültiges JSON. Bitte erneut versuchen oder von Hand erfassen.", ex);
        }
    }

    public static ReceiptSuggestion Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Unreadable();
        }

        var warnings = new List<string>();
        var suggestion = new ReceiptSuggestion
        {
            SupplierName = Text(root, "supplier_name"),
            Iban = Text(root, "supplier_iban"),
            InvoiceNumber = Text(root, "invoice_number"),
            InvoiceDate = Date(root, "invoice_date"),
            ServiceFrom = Date(root, "service_from"),
            ServiceTo = Date(root, "service_to"),
            DueOn = Date(root, "due_date"),
            Gross = Amount(root, "gross"),
            Net = Amount(root, "net"),
            Vat = Amount(root, "vat"),
            CostType = CostTypeField(root, "cost_type"),
            Section35a = Section(root),
            LaborCosts = Amount(root, "labor_costs"),
            FuelQuantity = Amount(root, "fuel_quantity"),
            FuelUnit = Text(root, "fuel_unit"),
            Co2Kg = Amount(root, "co2_kg"),
            Lines = Lines(root),
            Warnings = warnings,
        };

        // A response without any recognised field is not a result (e.g. {"error": …} or an empty object).
        string[] names = ["supplier_name", "invoice_number", "invoice_date", "gross", "net", "vat", "cost_type"];
        if (!names.Any(n => root.TryGetProperty(n, out _)))
        {
            throw Unreadable();
        }

        return suggestion;
    }

    /// <summary>Amount from a number or text ("1.234,56 €", "1,234.56", "-12,5", "12,50-").</summary>
    public static decimal? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = new string(text.Where(c => char.IsDigit(c) || c is ',' or '.' or '-' or '−').ToArray()).Replace('−', '-');
        var negative = value.StartsWith('-') || value.EndsWith('-');
        value = value.Trim('-');
        if (value.Length == 0 || value.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        var lastComma = value.LastIndexOf(',');
        var lastDot = value.LastIndexOf('.');
        string normalized;
        if (lastComma >= 0 && lastDot >= 0)
        {
            // The separator that comes last is the decimal separator.
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousands = decimalSeparator == ',' ? '.' : ',';
            normalized = value.Replace(thousands.ToString(), "", StringComparison.Ordinal).Replace(decimalSeparator, '.');
        }
        else if (lastComma >= 0 || lastDot >= 0)
        {
            var separator = lastComma >= 0 ? ',' : '.';
            var count = value.Count(c => c == separator);
            var digitsAfter = value.Length - value.LastIndexOf(separator) - 1;

            // Several identical separators or exactly three digits after a dot are thousands separators (German documents).
            normalized = count > 1 || (separator == '.' && digitsAfter == 3)
                ? value.Replace(separator.ToString(), "", StringComparison.Ordinal)
                : value.Replace(separator, '.');
        }
        else
        {
            normalized = value;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return negative ? -amount : amount;
    }

    public static DateOnly? ParseDate(string? text) =>
        !string.IsNullOrWhiteSpace(text) && DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateOnly.FromDateTime(date)
            : null;

    /// <summary>The JSON object in the output: strips code fences and text before/after the braces.</summary>
    private static string? ExtractJson(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var start = output.IndexOf('{', StringComparison.Ordinal);
        var end = output.LastIndexOf('}');
        return start >= 0 && end > start ? output[start..(end + 1)] : null;
    }

    private static ExtractionException Unreadable() =>
        new("Die Antwort des Modells konnte nicht ausgewertet werden. Bitte erneut versuchen oder von Hand erfassen.");

    private static bool Field(JsonElement root, string name, out JsonElement value, out double confidence)
    {
        value = default;
        confidence = MissingConfidence;
        if (!root.TryGetProperty(name, out var field) || field.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        // {"value": …, "confidence": …} or just the bare value.
        if (field.ValueKind == JsonValueKind.Object)
        {
            if (!field.TryGetProperty("value", out value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return false;
            }

            if (field.TryGetProperty("confidence", out var c))
            {
                confidence = Confidence(c);
            }

            return true;
        }

        value = field;
        return true;
    }

    private static double Confidence(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            return number is > 1 and <= 100 ? number / 100 : Math.Clamp(number, 0, 1);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()!.Trim().TrimEnd('%');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed is > 1 and <= 100 ? parsed / 100 : Math.Clamp(parsed, 0, 1);
            }

            return text.ToLowerInvariant() switch
            {
                "high" or "hoch" => 0.9,
                "medium" or "mittel" => 0.6,
                "low" or "niedrig" => 0.3,
                _ => MissingConfidence,
            };
        }

        return MissingConfidence;
    }

    private static Suggested<string> Text(JsonElement root, string name)
    {
        if (!Field(root, name, out var value, out var confidence))
        {
            return default;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? default : new Suggested<string>(Truncate(text.Trim()), confidence);
    }

    private static Suggested<DateOnly?> Date(JsonElement root, string name)
    {
        if (!Field(root, name, out var value, out var confidence) || value.ValueKind != JsonValueKind.String)
        {
            return default;
        }

        return ParseDate(value.GetString()) is { } date ? new Suggested<DateOnly?>(date, confidence) : default;
    }

    private static Suggested<decimal?> Amount(JsonElement root, string name)
    {
        if (!Field(root, name, out var value, out var confidence))
        {
            return default;
        }

        decimal? amount = value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => ParseAmount(value.GetString()),
            _ => null,
        };
        return amount is null ? default : new Suggested<decimal?>(amount, confidence);
    }

    private static Suggested<string> CostTypeField(JsonElement root, string name)
    {
        var text = Text(root, name);
        if (!text.HasValue)
        {
            return text;
        }

        return new Suggested<string>(CostTypeKey(text.Value!), text.Confidence);
    }

    private static Suggested<Section35a?> Section(JsonElement root)
    {
        if (!Field(root, "section_35a", out var value, out var confidence) || value.ValueKind != JsonValueKind.String)
        {
            return default;
        }

        Section35a? kind = value.GetString()?.Trim().ToLowerInvariant() switch
        {
            "none" or "keine" => Section35a.None,
            "household_service" or "householdservice" => Section35a.HouseholdService,
            "tradesman_service" or "tradesmanservice" => Section35a.TradesmanService,
            _ => null,
        };
        return kind is null ? default : new Suggested<Section35a?>(kind, confidence);
    }

    private static List<SuggestedLine> Lines(JsonElement root)
    {
        var lines = new List<SuggestedLine>();
        if (!root.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        foreach (var item in array.EnumerateArray().Take(50))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            decimal? amount = null;
            if (item.TryGetProperty("amount", out var a))
            {
                amount = a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var number) ? number
                    : a.ValueKind == JsonValueKind.String ? ParseAmount(a.GetString()) : null;
            }

            var costType = item.TryGetProperty("cost_type", out var k) && k.ValueKind == JsonValueKind.String ? CostTypeKey(k.GetString()!) : null;
            if (!string.IsNullOrWhiteSpace(description))
            {
                lines.Add(new SuggestedLine(Truncate(description.Trim()), amount, costType));
            }
        }

        return lines;
    }

    /// <summary>Key from the catalog; models sometimes answer with the name instead of the key.</summary>
    private static string CostTypeKey(string value)
    {
        var text = value.Trim();
        return CostTypes.Find(text)?.Key
            ?? CostTypes.All.FirstOrDefault(k => string.Equals(k.Name, text, StringComparison.OrdinalIgnoreCase))?.Key
            ?? text;
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];
}
