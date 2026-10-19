using MietHahn.Domain.Bank;
using MietHahn.Infrastructure.Bank.Import;

namespace MietHahn.Integration.Tests.Bank;

/// <summary>Parsers against anonymized sample files (no database).</summary>
public class BankFileParserTests
{
    private const string Account = "DE89370400440532013000";
    private const string AnnaIban = "DE93370501981234567890";
    private const string MunicipalUtilityIban = "DE83500105175555555555";

    [Fact]
    public void Camt053_Version_02()
    {
        var file = Read("camt053_v02.xml");

        Assert.Equal(BankFormat.Camt053, file.Format);
        Assert.Equal(Account, file.MoveOut.Iban);
        var u = file.MoveOut.Transactions;
        Assert.Equal(4, u.Count); // pending transaction skipped, batch entry split into two transactions
        Assert.Single(file.MoveOut.Hints);

        Assert.Equal(new ParsedTransaction(new DateOnly(2026, 2, 2), 770m, "Miete Februar 2026 Wohnung 1 Lindenweg 3", new DateOnly(2026, 2, 2), "EUR",
            "Anna Schmidt", AnnaIban, "COLSDE33XXX", "GUTSCHR. UEBERWEISUNG", "MIETE-2026-02"), u[0]);
        Assert.Equal((-84.50m, "Stadtwerke Musterstadt", MunicipalUtilityIban, "SW-4711", "DE98ZZZ09999999999"), (u[1].Amount, u[1].CounterpartyName, u[1].CounterpartyIban, u[1].MandateReference, u[1].CreditorId));
        Assert.Null(u[1].EndToEndId); // NOTPROVIDED
        Assert.Equal([("Ben Weber", 770m), ("Clara Vogel", 770m)], u.Skip(2).Select(x => (x.CounterpartyName, x.Amount)));
    }

    [Fact]
    public void Camt053_Version_08()
    {
        var u = Read("camt053_v08.xml").MoveOut.Transactions;

        Assert.Equal(2, u.Count);
        Assert.Equal((new DateOnly(2026, 3, 3), "Anna Schmidt", "COLSDE33XXX", "MIETE-2026-03"), (u[0].BookingDate, u[0].CounterpartyName, u[0].CounterpartyBic, u[0].EndToEndId));
        Assert.Equal(("Stadtwerke Musterstadt", "DE98ZZZ09999999999", -84.50m), (u[1].CounterpartyName, u[1].CreditorId, u[1].Amount));
    }

    [Fact]
    public void Mt940_with_structured_field_86()
    {
        var file = Read("mt940.sta");

        Assert.Equal(BankFormat.Mt940, file.Format);
        var u = file.MoveOut.Transactions;
        Assert.Equal(3, u.Count);
        Assert.Equal(new ParsedTransaction(new DateOnly(2026, 2, 2), 770m, "Miete Februar 2026 Wohnung 1 Lindenweg 3", new DateOnly(2026, 2, 2), "EUR",
            "Anna Schmidt", AnnaIban, "COLSDE33XXX", "GUTSCHR. UEBERWEISUNG", "MIETE-2026-02"), u[0]);
        Assert.Equal((-84.50m, "Abschlag Strom Allgemein 02/2026", "SW-4711", "DE98ZZZ09999999999", "FOLGELASTSCHRIFT"), (u[1].Amount, u[1].Purpose, u[1].MandateReference, u[1].CreditorId, u[1].BookingText));
        Assert.Null(u[1].EndToEndId);
        // Value date 31 Dec 2025, booking 2 Jan → in the following year
        Assert.Equal((new DateOnly(2026, 1, 2), new DateOnly(2025, 12, 31), "Kaution Rest", "Ben Weber"), (u[2].BookingDate, u[2].ValueDate, u[2].Purpose, u[2].CounterpartyName));
    }

    [Theory]
    [InlineData("sparkasse.csv", "Sparkasse (CSV-CAMT)")]
    [InlineData("volksbank.csv", "Volksbank / Raiffeisenbank")]
    [InlineData("dkb.csv", "DKB")]
    [InlineData("ing.csv", "ING")]
    [InlineData("comdirect.csv", "comdirect")]
    public void Csv_profile_is_detected_and_parsed(string file, string profile)
    {
        var read = Read(file);

        Assert.Equal(BankFormat.Csv, read.Format);
        Assert.Equal(profile, read.Profile);
        var rent = read.MoveOut.Transactions.First(u => u.Amount > 0);
        Assert.Equal((new DateOnly(2026, 2, 2), 770m, "Anna Schmidt"), (rent.BookingDate, rent.Amount, rent.CounterpartyName));
        Assert.StartsWith("Miete Februar 2026", rent.Purpose, StringComparison.Ordinal);
        var stream = read.MoveOut.Transactions.Single(u => u.Amount < 0);
        Assert.Equal(-84.50m, stream.Amount);
        Assert.StartsWith("Stadtwerke M", stream.CounterpartyName, StringComparison.Ordinal);
        Assert.Equal("Abschlag Strom Allgemein 02/2026", stream.Purpose);
    }

    [Fact]
    public void Csv_details_per_bank()
    {
        var sparkasse = Read("sparkasse.csv").MoveOut;
        Assert.Equal(2, sparkasse.Transactions.Count); // pending skipped
        Assert.Equal("Stadtwerke Müllerstadt", sparkasse.Transactions[1].CounterpartyName); // Windows-1252
        Assert.Equal(("SW-4711", "DE98ZZZ09999999999", "MIETE-2026-02"), (sparkasse.Transactions[1].MandateReference, sparkasse.Transactions[1].CreditorId, sparkasse.Transactions[0].EndToEndId));
        Assert.Equal(AnnaIban, sparkasse.Transactions[0].CounterpartyIban);

        var volksbank = Read("volksbank.csv").MoveOut.Transactions;
        Assert.Equal(("MIETE-2026-02", "Miete Februar 2026"), (volksbank[0].EndToEndId, volksbank[0].Purpose)); // SEPA references split

        var dkb = Read("dkb.csv").MoveOut;
        Assert.Equal(Account, dkb.Iban); // from the header
        Assert.Equal(2, dkb.Transactions.Count);
        Assert.Equal("Stadtwerke Musterstadt", dkb.Transactions[1].CounterpartyName); // outgoing: recipient

        var ing = Read("ing.csv").MoveOut;
        Assert.Equal(Account, ing.Iban);
        Assert.Equal(3, ing.Transactions.Count);

        var comdirect = Read("comdirect.csv").MoveOut;
        Assert.Equal(2, comdirect.Transactions.Count); // "open" entries and balances skipped
    }

    [Fact]
    public void Identical_transactions_in_one_file_are_not_duplicates_but_reimport_is()
    {
        var transactions = Read("ing.csv").MoveOut.Transactions;

        var first = TransactionFingerprint.Calculate(Account, transactions);
        var second = TransactionFingerprint.Calculate("DE89 3704 0044 0532 0130 00", transactions);

        Assert.Equal(3, first.Distinct().Count());
        Assert.Equal(first, second);
        Assert.NotEqual(first, TransactionFingerprint.Calculate("DE02120300000000202051", transactions));
    }

    [Theory]
    [InlineData("-1.234,56", ',', -1234.56)]
    [InlineData("770", ',', 770)]
    [InlineData("+12,5 €", ',', 12.5)]
    [InlineData("1,234.56", '.', 1234.56)]
    public void Csv_amounts(string text, char decimalSeparator, double expected) => Assert.Equal((decimal)expected, CsvParser.Amount(text, decimalSeparator));

    [Fact]
    public void Unknown_format_and_malformed_XML()
    {
        Assert.Throws<BankFileErrorException>(() => BankFileReader.Read("Hallo;Welt\n1;2\n"u8.ToArray(), CsvDefaults.All));
        Assert.Throws<BankFileErrorException>(() => BankFileReader.Read("<Document><BkToCstmrStmt>"u8.ToArray(), CsvDefaults.All));
        Assert.Throws<BankFileErrorException>(() => BankFileReader.Read([], CsvDefaults.All));
    }

    private static ParsedFile Read(string file) =>
        BankFileReader.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "TestFiles", file)), CsvDefaults.All);
}
