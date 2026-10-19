using MietHahn.Domain.Bank;
using MietHahn.Infrastructure.Bank.Import;

namespace MietHahn.Integration.Tests.Bank;

/// <summary>Parser gegen anonymisierte Beispieldateien (ohne Datenbank).</summary>
public class BankdateiParserTests
{
    private const string Konto = "DE89370400440532013000";
    private const string AnnaIban = "DE93370501981234567890";
    private const string StadtwerkeIban = "DE83500105175555555555";

    [Fact]
    public void Camt053_Version_02()
    {
        var datei = Lesen("camt053_v02.xml");

        Assert.Equal(Bankformat.Camt053, datei.Format);
        Assert.Equal(Konto, datei.Auszug.Iban);
        var u = datei.Auszug.Umsaetze;
        Assert.Equal(4, u.Count); // vorgemerkter Umsatz übersprungen, Sammler in zwei Umsätze zerlegt
        Assert.Single(datei.Auszug.Hinweise);

        Assert.Equal(new GelesenerUmsatz(new DateOnly(2026, 2, 2), 770m, "Miete Februar 2026 Wohnung 1 Lindenweg 3", new DateOnly(2026, 2, 2), "EUR",
            "Anna Schmidt", AnnaIban, "COLSDE33XXX", "GUTSCHR. UEBERWEISUNG", "MIETE-2026-02"), u[0]);
        Assert.Equal((-84.50m, "Stadtwerke Musterstadt", StadtwerkeIban, "SW-4711", "DE98ZZZ09999999999"), (u[1].Betrag, u[1].GegenName, u[1].GegenIban, u[1].Mandatsreferenz, u[1].GlaeubigerId));
        Assert.Null(u[1].EndToEndId); // NOTPROVIDED
        Assert.Equal([("Ben Weber", 770m), ("Clara Vogel", 770m)], u.Skip(2).Select(x => (x.GegenName, x.Betrag)));
    }

    [Fact]
    public void Camt053_Version_08()
    {
        var u = Lesen("camt053_v08.xml").Auszug.Umsaetze;

        Assert.Equal(2, u.Count);
        Assert.Equal((new DateOnly(2026, 3, 3), "Anna Schmidt", "COLSDE33XXX", "MIETE-2026-03"), (u[0].Buchungsdatum, u[0].GegenName, u[0].GegenBic, u[0].EndToEndId));
        Assert.Equal(("Stadtwerke Musterstadt", "DE98ZZZ09999999999", -84.50m), (u[1].GegenName, u[1].GlaeubigerId, u[1].Betrag));
    }

    [Fact]
    public void Mt940_mit_strukturiertem_Feld_86()
    {
        var datei = Lesen("mt940.sta");

        Assert.Equal(Bankformat.Mt940, datei.Format);
        var u = datei.Auszug.Umsaetze;
        Assert.Equal(3, u.Count);
        Assert.Equal(new GelesenerUmsatz(new DateOnly(2026, 2, 2), 770m, "Miete Februar 2026 Wohnung 1 Lindenweg 3", new DateOnly(2026, 2, 2), "EUR",
            "Anna Schmidt", AnnaIban, "COLSDE33XXX", "GUTSCHR. UEBERWEISUNG", "MIETE-2026-02"), u[0]);
        Assert.Equal((-84.50m, "Abschlag Strom Allgemein 02/2026", "SW-4711", "DE98ZZZ09999999999", "FOLGELASTSCHRIFT"), (u[1].Betrag, u[1].Verwendungszweck, u[1].Mandatsreferenz, u[1].GlaeubigerId, u[1].Buchungstext));
        Assert.Null(u[1].EndToEndId);
        // Valuta 31.12.2025, Buchung 02.01. → im Folgejahr
        Assert.Equal((new DateOnly(2026, 1, 2), new DateOnly(2025, 12, 31), "Kaution Rest", "Ben Weber"), (u[2].Buchungsdatum, u[2].Valutadatum, u[2].Verwendungszweck, u[2].GegenName));
    }

    [Theory]
    [InlineData("sparkasse.csv", "Sparkasse (CSV-CAMT)")]
    [InlineData("volksbank.csv", "Volksbank / Raiffeisenbank")]
    [InlineData("dkb.csv", "DKB")]
    [InlineData("ing.csv", "ING")]
    [InlineData("comdirect.csv", "comdirect")]
    public void Csv_Profil_wird_erkannt_und_gelesen(string datei, string profil)
    {
        var gelesen = Lesen(datei);

        Assert.Equal(Bankformat.Csv, gelesen.Format);
        Assert.Equal(profil, gelesen.Profil);
        var miete = gelesen.Auszug.Umsaetze.First(u => u.Betrag > 0);
        Assert.Equal((new DateOnly(2026, 2, 2), 770m, "Anna Schmidt"), (miete.Buchungsdatum, miete.Betrag, miete.GegenName));
        Assert.StartsWith("Miete Februar 2026", miete.Verwendungszweck, StringComparison.Ordinal);
        var strom = gelesen.Auszug.Umsaetze.Single(u => u.Betrag < 0);
        Assert.Equal(-84.50m, strom.Betrag);
        Assert.StartsWith("Stadtwerke M", strom.GegenName, StringComparison.Ordinal);
        Assert.Equal("Abschlag Strom Allgemein 02/2026", strom.Verwendungszweck);
    }

    [Fact]
    public void Csv_Details_je_Bank()
    {
        var sparkasse = Lesen("sparkasse.csv").Auszug;
        Assert.Equal(2, sparkasse.Umsaetze.Count); // vorgemerkt übersprungen
        Assert.Equal("Stadtwerke Müllerstadt", sparkasse.Umsaetze[1].GegenName); // Windows-1252
        Assert.Equal(("SW-4711", "DE98ZZZ09999999999", "MIETE-2026-02"), (sparkasse.Umsaetze[1].Mandatsreferenz, sparkasse.Umsaetze[1].GlaeubigerId, sparkasse.Umsaetze[0].EndToEndId));
        Assert.Equal(AnnaIban, sparkasse.Umsaetze[0].GegenIban);

        var volksbank = Lesen("volksbank.csv").Auszug.Umsaetze;
        Assert.Equal(("MIETE-2026-02", "Miete Februar 2026"), (volksbank[0].EndToEndId, volksbank[0].Verwendungszweck)); // SEPA-Kennungen zerlegt

        var dkb = Lesen("dkb.csv").Auszug;
        Assert.Equal(Konto, dkb.Iban); // aus dem Vorspann
        Assert.Equal(2, dkb.Umsaetze.Count);
        Assert.Equal("Stadtwerke Musterstadt", dkb.Umsaetze[1].GegenName); // Ausgang: Empfänger

        var ing = Lesen("ing.csv").Auszug;
        Assert.Equal(Konto, ing.Iban);
        Assert.Equal(3, ing.Umsaetze.Count);

        var comdirect = Lesen("comdirect.csv").Auszug;
        Assert.Equal(2, comdirect.Umsaetze.Count); // „offen“ und Kontostände übersprungen
    }

    [Fact]
    public void Gleiche_Umsaetze_in_einer_Datei_sind_kein_Duplikat_der_erneute_Import_schon()
    {
        var umsaetze = Lesen("ing.csv").Auszug.Umsaetze;

        var erster = Umsatzfingerabdruck.Berechnen(Konto, umsaetze);
        var zweiter = Umsatzfingerabdruck.Berechnen("DE89 3704 0044 0532 0130 00", umsaetze);

        Assert.Equal(3, erster.Distinct().Count());
        Assert.Equal(erster, zweiter);
        Assert.NotEqual(erster, Umsatzfingerabdruck.Berechnen("DE02120300000000202051", umsaetze));
    }

    [Theory]
    [InlineData("-1.234,56", ',', -1234.56)]
    [InlineData("770", ',', 770)]
    [InlineData("+12,5 €", ',', 12.5)]
    [InlineData("1,234.56", '.', 1234.56)]
    public void Csv_Betraege(string text, char dezimal, double erwartet) => Assert.Equal((decimal)erwartet, CsvParser.Betrag(text, dezimal));

    [Fact]
    public void Unbekanntes_Format_und_fehlerhaftes_XML()
    {
        Assert.Throws<BankdateiFehlerException>(() => Bankdateileser.Lesen("Hallo;Welt\n1;2\n"u8.ToArray(), CsvVorgaben.Alle));
        Assert.Throws<BankdateiFehlerException>(() => Bankdateileser.Lesen("<Document><BkToCstmrStmt>"u8.ToArray(), CsvVorgaben.Alle));
        Assert.Throws<BankdateiFehlerException>(() => Bankdateileser.Lesen([], CsvVorgaben.Alle));
    }

    private static GeleseneDatei Lesen(string datei) =>
        Bankdateileser.Lesen(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Bank", "Testdateien", datei)), CsvVorgaben.Alle);
}
