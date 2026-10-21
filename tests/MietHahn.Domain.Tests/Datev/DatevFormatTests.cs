using System.Text;
using MietHahn.Domain.Common;
using MietHahn.Domain.Datev;

namespace MietHahn.Domain.Tests.Datev;

public class DatevFormatTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 20, 9, 30, 15, 123, TimeSpan.Zero);

    private static DatevHeader Header(int accountLength = 4, bool fixedPosting = false, string? dictation = null) =>
        new(1001, 4711, new DateOnly(2026, 1, 1), accountLength, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "MietHahn 2026", dictation, fixedPosting, DatevChart.Skr03, Now, "Admin");

    private static DatevBooking Booking(decimal amount = 123.4m, bool debit = true, int account = 1200, int counter = 8105, string number = "RE-1", string text = "Miete", DateOnly? date = null) =>
        new(date ?? new DateOnly(2026, 3, 5), amount, debit, account, counter, number, text, "payment:x");

    private static string[] Lines(byte[] file) => Encoding.GetEncoding(1252).GetString(file).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Header_has_31_fields_with_the_extf_identification_and_client_data()
    {
        var header = DatevFormat.HeaderLine(Header(fixedPosting: true, dictation: "AH"));
        var fields = header.Split(';');

        Assert.Equal(31, fields.Length);
        Assert.Equal(["\"EXTF\"", "700", "21", "\"Buchungsstapel\"", "13", "20270120093015123"], fields[..6]);
        Assert.Equal(["1001", "4711", "20260101", "4", "20260101", "20261231"], fields[10..16]);
        Assert.Equal(["\"MietHahn 2026\"", "\"AH\"", "1", "0", "1", "\"EUR\""], fields[16..22]);
        Assert.Equal("\"03\"", fields[26]);
    }

    [Fact]
    public void Row_uses_decimal_comma_quoted_text_and_the_day_month_date()
    {
        var row = DatevFormat.Row(Booking(), Header());

        Assert.Equal("123,40;\"S\";\"EUR\";;;;1200;8105;;0503;\"RE-1\";;;\"Miete\"", row);
        Assert.Equal(DatevFormat.Columns.Count, row.Split(';').Length);
        Assert.StartsWith("1000,00;\"H\"", DatevFormat.Row(Booking(1000m, debit: false), Header()), StringComparison.Ordinal);
    }

    [Fact]
    public void Accounts_are_padded_to_the_account_length()
    {
        var row = DatevFormat.Row(Booking(account: 1200, counter: 8105), Header(accountLength: 6));

        Assert.Contains(";120000;810500;", row, StringComparison.Ordinal);
    }

    [Fact]
    public void File_is_windows_1252_with_crlf_header_columns_and_rows()
    {
        var file = DatevFormat.Write(Header(), [Booking(text: "Müller & Söhne")]);

        Assert.Contains((byte)0xFC, file); // ü
        Assert.DoesNotContain((byte)0xC3, file); // no UTF-8
        var lines = Lines(file);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Umsatz (ohne Soll/Haben-Kz);Soll/Haben-Kennzeichen;", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("\"Müller & Söhne\"", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Texts_are_cleaned_quoted_and_cut_to_the_field_length()
    {
        var long60 = new string('x', 80);
        var row = DatevFormat.Row(Booking(number: new string('9', 50), text: "Zeile 1\nZeile \"2\"" + long60), Header());

        Assert.Equal(DatevFormat.Columns.Count, row.Split(';').Length);
        Assert.Contains(new string('9', DatevFormat.MaxDocumentNumberLength) + "\"", row, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row);
        Assert.Contains("\"Zeile 1 Zeile \"\"2\"\"", row, StringComparison.Ordinal);
        Assert.Equal(DatevFormat.MaxTextLength, DatevFormat.Clean(long60, DatevFormat.MaxTextLength).Length);
    }

    [Fact]
    public void Validation_finds_wrong_amounts_accounts_and_dates()
    {
        Assert.Empty(DatevFormat.Validate(Header(), [Booking()]));
        Assert.NotEmpty(DatevFormat.Validate(Header(), [Booking(0m)]));
        Assert.NotEmpty(DatevFormat.Validate(Header(), [Booking(1.234m)]));
        Assert.NotEmpty(DatevFormat.Validate(Header(), [Booking(account: 12000)])); // five digits with account length 4
        Assert.NotEmpty(DatevFormat.Validate(Header(), [Booking(account: 1200, counter: 1200)]));
        Assert.NotEmpty(DatevFormat.Validate(Header(), [Booking(date: new DateOnly(2027, 1, 1))]));
        Assert.Throws<RuleViolationException>(() => DatevFormat.Write(Header(), [Booking(-5m)]));
    }

    [Fact]
    public void Settings_are_validated()
    {
        var settings = new DatevSettings();
        Assert.False(settings.IsComplete);

        settings.Set(1001, 4711, DatevChart.Skr04, 5, 4, " ah ");

        Assert.True(settings.IsComplete);
        Assert.Equal("AH", settings.DictationCode);
        Assert.Throws<RuleViolationException>(() => settings.Set(1000, 1, DatevChart.Skr03, 4, 1, null));
        Assert.Throws<RuleViolationException>(() => settings.Set(1001, 100_000, DatevChart.Skr03, 4, 1, null));
        Assert.Throws<RuleViolationException>(() => settings.Set(1001, 1, DatevChart.Skr03, 9, 1, null));
        Assert.Throws<RuleViolationException>(() => settings.Set(1001, 1, DatevChart.Skr03, 4, 13, null));
    }

    [Fact]
    public void Every_cost_type_and_income_category_has_a_proposal_that_fits_four_digits()
    {
        foreach (var chart in Enum.GetValues<DatevChart>())
        {
            foreach (var key in DatevAccountKeys.All)
            {
                var account = DatevAccountKeys.Default(chart, key);
                Assert.InRange(account, 1000, 9999);
                Assert.NotEqual(string.Empty, DatevAccountKeys.Label(key));
            }
        }

        Assert.Equal(1200, DatevAccountKeys.Default(DatevChart.Skr03, DatevAccountKeys.Bank));
        Assert.Equal(1800, DatevAccountKeys.Default(DatevChart.Skr04, DatevAccountKeys.Bank));
    }
}
