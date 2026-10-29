using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Elocaro.Domain.Leases;
using Elocaro.Infrastructure.Common;

namespace Elocaro.Infrastructure.Setup;

/// <summary>One line of the import file after reading; <see cref="Errors"/> lists what could not be understood.</summary>
internal sealed record ImportLine(
    int Line,
    string Property,
    string? Street,
    string? PostalCode,
    string? City,
    string Unit,
    string? Location,
    decimal? Area,
    string? FirstName,
    string? LastName,
    string? Email,
    DateOnly? Start,
    decimal? BaseRent,
    decimal? OperatingCosts,
    decimal? HeatingCosts,
    decimal? Deposit,
    decimal? DepositReceived,
    decimal? Balance,
    IReadOnlyList<string> Errors)
{
    public bool HasTenant => !string.IsNullOrWhiteSpace(LastName);

    /// <summary>Standard, Staffel- or Indexmiete; empty column = standard.</summary>
    public RentModel Model { get; init; }

    /// <summary>Index rent: month (first day) and value of the base index.</summary>
    public DateOnly? IndexMonth { get; init; }

    public decimal? IndexValue { get; init; }

    public DateOnly? FixedUntil { get; init; }

    public int? People { get; init; }

    /// <summary>Graduated rent: the steps, from the second rent on.</summary>
    public IReadOnlyList<(DateOnly From, decimal Rent)> Steps { get; init; } = [];
}

/// <summary>Reads the import CSV: any usual delimiter and encoding, German numbers and dates, tolerant column names.</summary>
internal static partial class ImportSheet
{
    public const string Template =
        "Objekt;Straße;PLZ;Ort;Einheit;Lage;Wohnfläche;Vorname;Nachname;E-Mail;Mietbeginn;Kaltmiete;Betriebskosten;Heizkosten;Kaution;Kaution eingegangen;Saldo\r\n" +
        "Lindenweg 3;Lindenweg 3;50667;Köln;Wohnung 1;EG links;55,5;Anna;Beispiel;anna@example.de;01.01.2024;650,00;150,00;80,00;1950,00;1950,00;0,00\r\n" +
        "Lindenweg 3;Lindenweg 3;50667;Köln;Wohnung 2;1. OG;62;;;;;;;;;;\r\n";

    private static readonly string[] MonthFormats = ["MM.yyyy", "M.yyyy", "yyyy-MM", "d.M.yyyy"];

    private static readonly string[] DateFormats = ["d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "yyyy-MM-dd", "ddMMyyyy"];

    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["property"] = ["objekt", "haus", "immobilie", "objektname"],
        ["street"] = ["strasse", "strassehausnummer", "strasseundhausnummer", "anschrift"],
        ["postalCode"] = ["plz", "postleitzahl"],
        ["city"] = ["ort", "stadt"],
        ["unit"] = ["einheit", "wohnung", "mietobjekt"],
        ["location"] = ["lage", "etage", "geschoss"],
        ["area"] = ["wohnflaeche", "flaeche", "wohnflaechem2", "flaechem2", "qm"],
        ["firstName"] = ["vorname"],
        ["lastName"] = ["nachname", "name", "mieter"],
        ["email"] = ["email", "emailadresse", "mail"],
        ["start"] = ["mietbeginn", "beginn", "einzug", "mietbeginnam"],
        ["baseRent"] = ["kaltmiete", "nettokaltmiete", "grundmiete", "miete"],
        ["operatingCosts"] = ["betriebskosten", "nebenkosten", "betriebskostenvorauszahlung", "nebenkostenvorauszahlung", "bk"],
        ["heatingCosts"] = ["heizkosten", "heizkostenvorauszahlung", "heizung"],
        ["deposit"] = ["kaution", "kautionvereinbart"],
        ["depositReceived"] = ["kautioneingegangen", "kautiongezahlt", "kautionbezahlt", "kautioneingang"],
        ["balance"] = ["saldo", "rueckstand", "saldovortrag", "offenerbetrag"],
        ["model"] = ["mietmodell", "modell", "mietart"],
        ["indexMonth"] = ["indexmonat", "indexbasismonat", "basisindexmonat"],
        ["indexValue"] = ["indexwert", "indexbasiswert", "basisindex"],
        ["fixedUntil"] = ["befristetbis", "befristung", "mietende"],
        ["people"] = ["personen", "bewohner", "anzahlpersonen"],
        ["steps"] = ["staffeln", "mietstaffeln"],
    };

    public static (IReadOnlyList<ImportLine> Lines, IReadOnlyList<string> FileErrors) Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = TextDecoding.Read(content);
        var rows = Split(text, Delimiter(text)).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count == 0)
        {
            return ([], ["Die Datei ist leer."]);
        }

        var header = rows[0].Select(Normalize).ToList();
        var map = Columns.ToDictionary(c => c.Key, c => header.FindIndex(h => c.Value.Contains(h, StringComparer.Ordinal)));
        var errors = new List<string>();
        if (map["property"] < 0)
        {
            errors.Add("Die Spalte „Objekt“ fehlt.");
        }

        if (map["unit"] < 0)
        {
            errors.Add("Die Spalte „Einheit“ fehlt.");
        }

        if (errors.Count > 0)
        {
            return ([], [.. errors, "Die erste Zeile muss die Spaltenüberschriften enthalten (siehe Vorlage)."]);
        }

        if (rows.Count == 1)
        {
            return ([], ["Die Datei enthält keine Zeilen unter den Überschriften."]);
        }

        var lines = new List<ImportLine>();
        for (var i = 1; i < rows.Count; i++)
        {
            lines.Add(ReadLine(rows[i], i + 1, map));
        }

        return (lines, []);
    }

    private static ImportLine ReadLine(List<string> row, int number, Dictionary<string, int> map)
    {
        var errors = new List<string>();
        string? Text(string key) => map[key] >= 0 && map[key] < row.Count && row[map[key]].Trim() is { Length: > 0 } value ? value : null;

        decimal? Number(string key, string label)
        {
            if (Text(key) is not { } value)
            {
                return null;
            }

            if (TryNumber(value, out var number))
            {
                return number;
            }

            errors.Add($"{label} „{value}“ ist keine Zahl.");
            return null;
        }

        DateOnly? Date(string key, string label)
        {
            if (Text(key) is not { } value)
            {
                return null;
            }

            if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            errors.Add($"{label} „{value}“ ist kein Datum (erwartet: TT.MM.JJJJ).");
            return null;
        }

        var property = Text("property");
        var unit = Text("unit");
        if (property is null)
        {
            errors.Add("Das Objekt fehlt.");
        }

        if (unit is null)
        {
            errors.Add("Die Einheit fehlt.");
        }

        var area = Number("area", "Wohnfläche");
        if (area is <= 0 or > 10000)
        {
            errors.Add("Die Wohnfläche muss größer als 0 sein.");
            area = null;
        }

        var start = Date("start", "Mietbeginn");
        var baseRent = Number("baseRent", "Kaltmiete");
        var operating = Number("operatingCosts", "Betriebskosten");
        var heating = Number("heatingCosts", "Heizkosten");
        var deposit = Number("deposit", "Kaution");
        var depositReceived = Number("depositReceived", "Kaution eingegangen");
        var balance = Number("balance", "Saldo");
        var lastName = Text("lastName");
        if (lastName is not null)
        {
            if (start is null && !errors.Any(e => e.StartsWith("Mietbeginn", StringComparison.Ordinal)))
            {
                errors.Add($"Für den Mieter „{lastName}“ fehlt der Mietbeginn.");
            }

            if (baseRent is null && !errors.Any(e => e.StartsWith("Kaltmiete", StringComparison.Ordinal)))
            {
                errors.Add($"Für den Mieter „{lastName}“ fehlt die Kaltmiete.");
            }
        }
        else if (start is not null || baseRent is not null || balance is not null)
        {
            errors.Add("Mietbeginn, Miete oder Saldo sind angegeben, aber ohne Mieter (Spalte „Nachname“).");
        }

        foreach (var (value, label) in new[] { (baseRent, "Kaltmiete"), (operating, "Betriebskosten"), (heating, "Heizkosten"), (deposit, "Kaution"), (depositReceived, "Kaution eingegangen") })
        {
            if (value is < 0)
            {
                errors.Add($"{label} darf nicht negativ sein.");
            }
        }

        var model = RentModel.Standard;
        if (Text("model") is { } modelText)
        {
            switch (Normalize(modelText))
            {
                case "standard" or "festmiete":
                    break;
                case "staffel" or "staffelmiete":
                    model = RentModel.Graduated;
                    break;
                case "index" or "indexmiete":
                    model = RentModel.Index;
                    break;
                default:
                    errors.Add($"Das Mietmodell „{modelText}“ ist unbekannt (Standard, Staffel oder Index).");
                    break;
            }
        }

        DateOnly? indexMonth = null;
        if (Text("indexMonth") is { } monthText)
        {
            if (DateOnly.TryParseExact(monthText, MonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            {
                indexMonth = new DateOnly(month.Year, month.Month, 1);
            }
            else
            {
                errors.Add($"Indexmonat „{monthText}“ ist kein Monat (erwartet: MM.JJJJ).");
            }
        }

        var indexValue = Number("indexValue", "Indexwert");
        var fixedUntil = Date("fixedUntil", "Befristet bis");
        int? people = null;
        if (Text("people") is { } peopleText)
        {
            if (int.TryParse(peopleText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is >= 1 and <= 30)
            {
                people = count;
            }
            else
            {
                errors.Add($"Personen „{peopleText}“ ist keine Anzahl zwischen 1 und 30.");
            }
        }

        var steps = new List<(DateOnly, decimal)>();
        if (Text("steps") is { } stepsText)
        {
            foreach (var part in stepsText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length == 2 && DateOnly.TryParseExact(pair[0], DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) && TryNumber(pair[1], out var rent) && rent > 0)
                {
                    steps.Add((from, rent));
                }
                else
                {
                    errors.Add($"Staffel „{part}“ ist ungültig (erwartet: TT.MM.JJJJ=Betrag, mehrere mit | getrennt).");
                }
            }
        }

        if (lastName is not null)
        {
            if (model == RentModel.Index && (indexMonth is null || indexValue is null))
            {
                errors.Add("Bei einer Indexmiete fehlen Indexmonat und Indexwert des Ausgangsindex.");
            }

            if (model == RentModel.Graduated && steps.Count == 0)
            {
                errors.Add("Bei einer Staffelmiete fehlt die Spalte „Staffeln“.");
            }

            if (fixedUntil is { } until && start is { } begin && until <= begin)
            {
                errors.Add("„Befristet bis“ liegt nicht nach dem Mietbeginn.");
            }
        }

        var email = Text("email");
        if (email is not null && !email.Contains('@', StringComparison.Ordinal))
        {
            errors.Add($"Die E-Mail-Adresse „{email}“ ist ungültig.");
            email = null;
        }

        return new ImportLine(number, property ?? "", Text("street"), Text("postalCode"), Text("city"), unit ?? "", Text("location"), area,
            Text("firstName"), lastName, email, start, baseRent, operating, heating, deposit, depositReceived, balance, errors)
        {
            Model = model,
            IndexMonth = indexMonth,
            IndexValue = indexValue,
            FixedUntil = fixedUntil,
            People = people,
            Steps = [.. steps.OrderBy(s => s.Item1)],
        };
    }

    /// <summary>German (1.234,56 / 650,5) and plain (650.5) numbers, optionally with € and spaces.</summary>
    internal static bool TryNumber(string text, out decimal value)
    {
        var cleaned = text.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (cleaned.Contains(',', StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        }
        else if (ThousandsOnly().IsMatch(cleaned))
        {
            cleaned = cleaned.Replace(".", "", StringComparison.Ordinal);
        }

        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static char Delimiter(string text)
    {
        var first = text.Split('\n', 2)[0];
        var candidates = new[] { ';', '\t', ',' };
        return candidates.OrderByDescending(c => first.Count(x => x == c)).First();
    }

    /// <summary>Lowercase, umlauts resolved, everything but letters and digits removed.</summary>
    private static string Normalize(string header)
    {
        var text = header.Trim().ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal)
            .Replace("²", "2", StringComparison.Ordinal);
        return NonAlphanumeric().Replace(text, "");
    }

    /// <summary>Quote-aware split: fields in double quotes may contain the delimiter and line breaks; "" is a quote.</summary>
    private static IEnumerable<List<string>> Split(string text, char delimiter)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsOnly();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
