using System.Globalization;
using System.Text;
using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;
using MietHahn.Domain.Tax;

namespace MietHahn.Domain.Datev;

public enum DatevChart
{
    Skr03 = 3,
    Skr04 = 4,
}

/// <summary>Client data and basic settings of the DATEV export (one row).</summary>
public sealed class DatevSettings : Entity
{
    public const int MinAccountLength = 4;
    public const int MaxAccountLength = 8;

    /// <summary>Consultant number (Beraternummer), 1001 to 9,999,999.</summary>
    public int ConsultantNumber { get; private set; }

    /// <summary>Client number (Mandantennummer), 1 to 99,999.</summary>
    public int ClientNumber { get; private set; }

    public DatevChart Chart { get; private set; } = DatevChart.Skr03;

    /// <summary>Length of the G/L accounts (4 to 8); shorter numbers are padded with zeros on the right.</summary>
    public int AccountLength { get; private set; } = MinAccountLength;

    /// <summary>Month the fiscal year starts in (1 = calendar year).</summary>
    public int FiscalYearStartMonth { get; private set; } = 1;

    /// <summary>Short code of the person dictating (Diktatkürzel), at most 2 characters.</summary>
    public string? DictationCode { get; private set; }

    public bool IsComplete => ConsultantNumber > 0 && ClientNumber > 0;

    public void Set(int consultantNumber, int clientNumber, DatevChart chart, int accountLength, int fiscalYearStartMonth, string? dictationCode)
    {
        if (consultantNumber is < 1001 or > 9_999_999)
        {
            throw new RuleViolationException("Die Beraternummer muss zwischen 1001 und 9999999 liegen.");
        }

        if (clientNumber is < 1 or > 99_999)
        {
            throw new RuleViolationException("Die Mandantennummer muss zwischen 1 und 99999 liegen.");
        }

        if (accountLength is < MinAccountLength or > MaxAccountLength)
        {
            throw new RuleViolationException("Die Sachkontenlänge muss zwischen 4 und 8 liegen.");
        }

        if (fiscalYearStartMonth is < 1 or > 12)
        {
            throw new RuleViolationException("Der Beginn des Wirtschaftsjahres muss ein Monat von 1 bis 12 sein.");
        }

        var code = string.IsNullOrWhiteSpace(dictationCode) ? null : dictationCode.Trim().ToUpperInvariant();
        if (code is { Length: > 2 })
        {
            throw new RuleViolationException("Das Diktatkürzel hat höchstens 2 Zeichen.");
        }

        ConsultantNumber = consultantNumber;
        ClientNumber = clientNumber;
        Chart = chart;
        AccountLength = accountLength;
        FiscalYearStartMonth = fiscalYearStartMonth;
        DictationCode = code;
    }
}

/// <summary>Account for a key (<see cref="DatevAccountKeys"/>), configurable per chart of accounts.</summary>
public sealed class DatevAccount : Entity
{
    private DatevAccount()
    {
        Key = "";
    }

    public DatevAccount(string key, int account)
    {
        Key = key;
        Account = account;
        Validate();
    }

    public string Key { get; private set; }

    public int Account { get; private set; }

    public void SetAccount(int account)
    {
        Account = account;
        Validate();
    }

    private void Validate()
    {
        if (Account is < 1 or > 99_999_999)
        {
            throw new RuleViolationException("Ein Sachkonto hat 1 bis 8 Ziffern.");
        }
    }
}

/// <summary>An export file that was created (with the file content, so that it can be downloaded again).</summary>
public sealed class DatevExport : Entity
{
    private DatevExport()
    {
        FileName = "";
        Hash = "";
        Content = [];
    }

    public DatevExport(DateOnly from, DateOnly to, int count, decimal total, bool fixedPosting, string fileName, byte[] content, string hash)
    {
        From = from;
        To = to;
        Count = count;
        Total = total;
        FixedPosting = fixedPosting;
        FileName = fileName;
        Content = content;
        Hash = hash;
    }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public int Count { get; private set; }

    /// <summary>Sum of the amounts (absolute values) for reconciliation with the consultant.</summary>
    public decimal Total { get; private set; }

    /// <summary>The header carries the "fixed" flag (Festschreibung).</summary>
    public bool FixedPosting { get; private set; }

    public string FileName { get; private set; }

    public byte[] Content { get; private set; }

    /// <summary>SHA-256 of the content (hex).</summary>
    public string Hash { get; private set; }
}

/// <summary>A source (payment share or cost item) that has been exported; prevents a double export.</summary>
public sealed class DatevExportedItem : Entity
{
    private DatevExportedItem()
    {
        SourceKey = "";
    }

    public DatevExportedItem(Guid exportId, string sourceKey)
    {
        ExportId = exportId;
        SourceKey = sourceKey;
    }

    public Guid ExportId { get; private set; }

    public string SourceKey { get; private set; }
}

/// <summary>Keys of the account mapping and their built-in proposals.</summary>
public static class DatevAccountKeys
{
    public const string Bank = "bank";

    public static string Income(AnlageVCategory category) => "income:" + category;

    public static string Cost(string costType) => "cost:" + costType;

    public static IReadOnlyList<AnlageVCategory> IncomeCategories { get; } =
        [AnlageVCategory.BaseRent, AnlageVCategory.AllocatedCosts, AnlageVCategory.AdditionalPayments, AnlageVCategory.Refunds, AnlageVCategory.OtherIncome];

    /// <summary>Label shown next to the key.</summary>
    public static string Label(string key) => key switch
    {
        Bank => "Bank (Mietkonto)",
        _ when key.StartsWith("income:", StringComparison.Ordinal) && Enum.TryParse<AnlageVCategory>(key["income:".Length..], out var category)
            => "Einnahmen: " + (AnlageVCategories.All.FirstOrDefault(c => c.Category == category)?.Label ?? category.ToString()),
        _ when key.StartsWith("cost:", StringComparison.Ordinal) => "Kosten: " + (CostTypes.Find(key["cost:".Length..])?.Name ?? key),
        _ => key,
    };

    /// <summary>All keys in display order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Bank, .. IncomeCategories.Select(Income), .. CostTypes.All.Select(k => Cost(k.Key))];

    /// <summary>
    /// Proposals only: the consultant decides on the accounts. The numbers follow the usual rental accounts of the
    /// standard charts and are to be adjusted to the client's chart.
    /// </summary>
    public static int Default(DatevChart chart, string key)
    {
        var skr03 = chart == DatevChart.Skr03;
        if (key == Bank)
        {
            return skr03 ? 1200 : 1800;
        }

        if (key.StartsWith("income:", StringComparison.Ordinal))
        {
            return Enum.TryParse<AnlageVCategory>(key["income:".Length..], out var category) && category is AnlageVCategory.AllocatedCosts or AnlageVCategory.AdditionalPayments or AnlageVCategory.Refunds
                ? skr03 ? 8110 : 4110
                : category == AnlageVCategory.OtherIncome ? skr03 ? 8200 : 4830 : skr03 ? 8105 : 4105;
        }

        var costType = key["cost:".Length..];
        return costType switch
        {
            "betrkv-01" => skr03 ? 4270 : 6340,
            "betrkv-02" or "betrkv-03" or "betrkv-11" => skr03 ? 4240 : 6325,
            "betrkv-04" or "betrkv-05" or "betrkv-06" => skr03 ? 4230 : 6320,
            "betrkv-09" or "betrkv-08" or "betrkv-10" or "betrkv-14" => skr03 ? 4250 : 6330,
            "betrkv-13" => skr03 ? 4360 : 6400,
            "instandhaltung" => skr03 ? 4260 : 6335,
            "verwaltung" => skr03 ? 4900 : 6850,
            "fahrtkosten" => skr03 ? 4670 : 6670,
            "bankgebuehren" => skr03 ? 4970 : 6855,
            "schuldzinsen" => skr03 ? 2100 : 7310,
            "beratung" => skr03 ? 4950 : 6825,
            _ when costType.StartsWith("betrkv-", StringComparison.Ordinal) => skr03 ? 4280 : 6345,
            _ => skr03 ? 4900 : 6850,
        };
    }
}

/// <summary>
/// One line of the posting batch. <see cref="Amount"/> is positive; <see cref="Debit"/> says whether the amount is
/// booked as debit (S) or credit (H) on <see cref="Account"/>.
/// </summary>
/// <param name="SourceKey">Stable key of the source, to recognise an item that has been exported already.</param>
public sealed record DatevBooking(
    DateOnly Date,
    decimal Amount,
    bool Debit,
    int Account,
    int CounterAccount,
    string DocumentNumber,
    string Text,
    string SourceKey);

/// <summary>Header data of the batch.</summary>
public sealed record DatevHeader(
    int ConsultantNumber,
    int ClientNumber,
    DateOnly FiscalYearStart,
    int AccountLength,
    DateOnly From,
    DateOnly To,
    string Designation,
    string? DictationCode,
    bool FixedPosting,
    DatevChart Chart,
    DateTimeOffset CreatedAt,
    string ExportedBy);

/// <summary>
/// DATEV format "Buchungsstapel" (EXTF, header version 700, category 21, format version 13) as CSV: semicolons, text in
/// quotes, decimal comma, CRLF. Only the leading fields up to the posting text are written; they are the ones the
/// import needs.
/// </summary>
public static class DatevFormat
{
    public const int MaxDocumentNumberLength = 36;
    public const int MaxTextLength = 60;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly Encoding Windows1252 = CreateEncoding();

    public static readonly IReadOnlyList<string> Columns =
    [
        "Umsatz (ohne Soll/Haben-Kz)", "Soll/Haben-Kennzeichen", "WKZ Umsatz", "Kurs", "Basis-Umsatz", "WKZ Basis-Umsatz",
        "Konto", "Gegenkonto (ohne BU-Schlüssel)", "BU-Schlüssel", "Belegdatum", "Belegfeld 1", "Belegfeld 2", "Skonto", "Buchungstext",
    ];

    /// <summary>Rules of the format; returns the problems in German (empty = ok).</summary>
    public static IReadOnlyList<string> Validate(DatevHeader header, IReadOnlyList<DatevBooking> bookings)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(bookings);
        var problems = new List<string>();
        if (header.To < header.From)
        {
            problems.Add("Das Ende des Zeitraums liegt vor dem Beginn.");
        }

        var limit = (long)Math.Pow(10, header.AccountLength);
        foreach (var booking in bookings)
        {
            if (booking.Amount <= 0 || decimal.Round(booking.Amount, 2) != booking.Amount || booking.Amount > 999_999_999.99m)
            {
                problems.Add($"{booking.SourceKey}: Der Betrag muss positiv sein, höchstens zwei Nachkommastellen haben und unter einer Milliarde liegen.");
            }

            foreach (var account in new[] { booking.Account, booking.CounterAccount })
            {
                if (account < 1 || account >= limit)
                {
                    problems.Add($"{booking.SourceKey}: Das Konto {account} passt nicht zur Sachkontenlänge {header.AccountLength}.");
                }
            }

            if (booking.Account == booking.CounterAccount)
            {
                problems.Add($"{booking.SourceKey}: Konto und Gegenkonto sind gleich.");
            }

            if (booking.Date < header.From || booking.Date > header.To)
            {
                problems.Add($"{booking.SourceKey}: Das Datum liegt außerhalb des Zeitraums.");
            }
        }

        return problems;
    }

    /// <summary>Complete file (header, column names, rows) in Windows-1252, as DATEV expects it.</summary>
    public static byte[] Write(DatevHeader header, IReadOnlyList<DatevBooking> bookings)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(bookings);
        if (Validate(header, bookings) is { Count: > 0 } problems)
        {
            throw new RuleViolationException(problems[0]);
        }

        var text = new StringBuilder();
        text.Append(HeaderLine(header)).Append("\r\n");
        text.Append(string.Join(';', Columns)).Append("\r\n");
        foreach (var booking in bookings)
        {
            text.Append(Row(booking, header)).Append("\r\n");
        }

        return Windows1252.GetBytes(text.ToString());
    }

    /// <summary>The encoding DATEV expects; characters it cannot represent become "?".</summary>
    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    public static string HeaderLine(DatevHeader h)
    {
        var fields = new string[31];
        Array.Fill(fields, "");
        fields[0] = Quote("EXTF");
        fields[1] = "700";
        fields[2] = "21";
        fields[3] = Quote("Buchungsstapel");
        fields[4] = "13";
        fields[5] = h.CreatedAt.UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        fields[7] = Quote("RE");
        fields[8] = Quote(Clean(h.ExportedBy, 25));
        fields[10] = h.ConsultantNumber.ToString(CultureInfo.InvariantCulture);
        fields[11] = h.ClientNumber.ToString(CultureInfo.InvariantCulture);
        fields[12] = h.FiscalYearStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        fields[13] = h.AccountLength.ToString(CultureInfo.InvariantCulture);
        fields[14] = h.From.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        fields[15] = h.To.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        fields[16] = Quote(Clean(h.Designation, 30));
        fields[17] = h.DictationCode is null ? "" : Quote(h.DictationCode);
        fields[18] = "1";
        fields[19] = "0";
        fields[20] = h.FixedPosting ? "1" : "0";
        fields[21] = Quote("EUR");
        fields[26] = Quote(h.Chart == DatevChart.Skr03 ? "03" : "04");
        return string.Join(';', fields);
    }

    public static string Row(DatevBooking b, DatevHeader header)
    {
        var fields = new string[14];
        Array.Fill(fields, "");
        fields[0] = b.Amount.ToString("0.00", German);
        fields[1] = Quote(b.Debit ? "S" : "H");
        fields[2] = Quote("EUR");
        fields[6] = Pad(b.Account, header.AccountLength);
        fields[7] = Pad(b.CounterAccount, header.AccountLength);
        fields[9] = b.Date.ToString("ddMM", CultureInfo.InvariantCulture);
        fields[10] = Quote(Clean(b.DocumentNumber, MaxDocumentNumberLength));
        fields[13] = Quote(Clean(b.Text, MaxTextLength));
        return string.Join(';', fields);
    }

    /// <summary>Accounts are written with the length of the chart; the digits are the account as entered, padded right.</summary>
    private static string Pad(int account, int length) => account.ToString(CultureInfo.InvariantCulture).PadRight(length, '0');

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Removes control characters and semicolons-breaking line breaks, shortens to the field length.</summary>
    public static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
    }
}
