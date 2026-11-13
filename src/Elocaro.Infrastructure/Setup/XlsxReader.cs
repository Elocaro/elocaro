using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Elocaro.Infrastructure.Setup;

/// <summary>
/// Reads the first worksheet of an Excel file (.xlsx) without a spreadsheet library and returns it as the CSV the import already understands
/// (UTF-8 with BOM, semicolon, German numbers, dates as dd.MM.yyyy). Formulas count with the value Excel stored; macros, styles and further sheets are ignored.
/// </summary>
internal static partial class XlsxReader
{
    private const int MaxRows = 20_000;
    private const int MaxColumns = 200;
    private const long MaxEntryBytes = 64L * 1024 * 1024;

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>An .xlsx file is a ZIP archive.</summary>
    public static bool Looks(byte[] content) => content is [0x50, 0x4B, 0x03, 0x04, ..];

    /// <summary>The first worksheet as import CSV; <see cref="InvalidDataException"/> if the file is not a usable workbook.</summary>
    public static byte[] ToCsv(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            return Encode(ReadRows(content));
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or KeyNotFoundException)
        {
            throw new InvalidDataException("Die Excel-Datei konnte nicht gelesen werden. Bitte als .xlsx speichern (nicht .xls) oder als CSV exportieren.", ex);
        }
    }

    private static List<List<string>> ReadRows(byte[] content)
    {
        using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
        var sheetPath = FirstSheetPath(archive);
        var shared = ReadSharedStrings(archive);
        var dateStyles = ReadDateStyles(archive);
        var sheet = Load(archive, sheetPath) ?? throw new InvalidDataException("Das Tabellenblatt fehlt.");

        var rows = new List<List<string>>();
        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var index = (int?)row.Attribute("r") ?? rows.Count + 1;
            if (index > MaxRows)
            {
                throw new InvalidDataException($"Die Tabelle hat mehr als {MaxRows} Zeilen.");
            }

            var cells = new List<string>();
            foreach (var cell in row.Elements(Main + "c"))
            {
                var column = ColumnIndex((string?)cell.Attribute("r"), cells.Count);
                if (column >= MaxColumns)
                {
                    continue;
                }

                while (cells.Count < column)
                {
                    cells.Add("");
                }

                cells.Add(CellText(cell, shared, dateStyles));
            }

            while (rows.Count < index - 1)
            {
                rows.Add([]);
            }

            rows.Add(cells);
        }

        while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private static string CellText(XElement cell, List<string> shared, HashSet<int> dateStyles)
    {
        var type = (string?)cell.Attribute("t");
        var value = (string?)cell.Element(Main + "v");
        switch (type)
        {
            case "s":
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < shared.Count ? shared[i] : "";
            case "inlineStr":
                return string.Concat(cell.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? []);
            case "b":
                return value == "1" ? "Ja" : "Nein";
            case "e":
                return "";
            case "str":
            case "d":
                return value ?? "";
        }

        if (value is null)
        {
            return "";
        }

        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return value;
        }

        var style = (int?)cell.Attribute("s") ?? 0;
        if (dateStyles.Contains(style) && number is >= 1 and < 2_958_466)
        {
            return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays((double)Math.Floor(number))).ToString("dd.MM.yyyy", German);
        }

        return decimal.Round(number, 6).ToString("0.######", German);
    }

    private static string FirstSheetPath(ZipArchive archive)
    {
        var workbook = Load(archive, "xl/workbook.xml") ?? throw new InvalidDataException("Keine Excel-Arbeitsmappe.");
        var first = workbook.Descendants(Main + "sheet").FirstOrDefault() ?? throw new InvalidDataException("Die Arbeitsmappe hat kein Tabellenblatt.");
        var id = (string?)first.Attribute(Rel + "id");
        var relations = Load(archive, "xl/_rels/workbook.xml.rels");
        var target = relations?.Descendants(PackageRel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == id)?.Attribute("Target")?.Value;
        if (target is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var document = Load(archive, "xl/sharedStrings.xml");
        return document is null
            ? []
            : [.. document.Descendants(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value)))];
    }

    /// <summary>Indexes of the cell formats (<c>s</c> attribute) that show a date.</summary>
    private static HashSet<int> ReadDateStyles(ZipArchive archive)
    {
        var result = new HashSet<int>();
        var styles = Load(archive, "xl/styles.xml");
        if (styles is null)
        {
            return result;
        }

        var custom = styles.Descendants(Main + "numFmt")
            .ToDictionary(n => (int?)n.Attribute("numFmtId") ?? -1, n => (string?)n.Attribute("formatCode") ?? "");
        var index = 0;
        foreach (var xf in styles.Descendants(Main + "cellXfs").Elements(Main + "xf"))
        {
            var id = (int?)xf.Attribute("numFmtId") ?? 0;
            if (IsDateFormat(id, custom))
            {
                result.Add(index);
            }

            index++;
        }

        return result;
    }

    private static bool IsDateFormat(int id, Dictionary<int, string> custom)
    {
        if (custom.TryGetValue(id, out var code))
        {
            var plain = QuotedOrBracketed().Replace(code, "");
            return plain.Any(c => c is 'y' or 'Y' or 'd' or 'D');
        }

        return id is >= 14 and <= 17 or 22 or >= 27 and <= 36 or >= 50 and <= 58;
    }

    private static XDocument? Load(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > MaxEntryBytes)
        {
            throw new InvalidDataException("Die Datei ist zu groß.");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return fallback;
        }

        var column = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            column = (column * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return column == 0 ? fallback : column - 1;
    }

    private static byte[] Encode(List<List<string>> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(';', row.Select(Quote)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Quote(string value) =>
        value.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QuotedOrBracketed();
}
