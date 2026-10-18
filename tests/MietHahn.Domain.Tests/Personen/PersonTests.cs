using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;

namespace MietHahn.Domain.Tests.Personen;

public class PersonTests
{
    [Fact]
    public void Anzeigename_mit_Titel_und_Firma()
    {
        Assert.Equal("Dr. Anna Schmidt", new Person("Schmidt", "Anna") { Titel = "Dr." }.Anzeigename);
        Assert.Equal("Schmidt, Anna", new Person("Schmidt", "Anna").Sortiername);
        Assert.Equal("Stadtwerke Köln GmbH (Kundenservice)", new Person("Kundenservice") { Firma = "Stadtwerke Köln GmbH" }.Anzeigename);
    }

    [Fact]
    public void Umzug_erhaelt_Voranschrift_und_liefert_neue_Anschrift_nach_Auszug()
    {
        var mieter = new Person("Schmidt", "Anna");
        mieter.AnschriftSetzen(new DateOnly(2020, 5, 1), "Altweg 1", "50667", "Köln");
        mieter.AnschriftSetzen(new DateOnly(2026, 3, 1), "Lindenweg 3", "50667", "Köln", "Wohnung 1");
        mieter.AnschriftSetzen(new DateOnly(2027, 8, 1), "Neue Straße 5", "10115", "Berlin");

        Assert.Equal("Altweg 1, 50667 Köln", mieter.AnschriftAm(new DateOnly(2026, 2, 28))!.Einzeilig);
        Assert.Equal("Wohnung 1, Lindenweg 3, 50667 Köln", mieter.AnschriftAm(new DateOnly(2026, 3, 1))!.Einzeilig);
        // Abrechnung 2027, versandt 2028: an die neue Anschrift
        Assert.Equal("Neue Straße 5, 10115 Berlin", mieter.AnschriftAm(new DateOnly(2028, 5, 1))!.Einzeilig);
        Assert.Equal(3, mieter.Anschriften.Count);
    }

    [Fact]
    public void Mehrere_IBANs_gueltig_und_ohne_Doppelte()
    {
        var mieter = new Person("Schmidt", "Anna");

        mieter.BankverbindungHinzufuegen("de02120300000000202051");
        mieter.BankverbindungHinzufuegen("DE89 3704 0044 0532 0130 00", "Hans Schmidt (Vater)", "cobadeffxxx");

        Assert.Equal(["DE02 1203 0000 0000 2020 51", "DE89 3704 0044 0532 0130 00"], mieter.Bankverbindungen.Select(b => b.Iban));
        Assert.Equal("COBADEFFXXX", mieter.Bankverbindungen[1].Bic);
        Assert.Throws<RegelverletzungException>(() => mieter.BankverbindungHinzufuegen("DE02120300000000202051"));
        var fehler = Assert.Throws<RegelverletzungException>(() => mieter.BankverbindungHinzufuegen("DE02120300000000202052"));
        Assert.Contains("keine gültige IBAN", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Anonymisieren_entfernt_alle_personenbezogenen_Daten()
    {
        var mieter = new Person("Schmidt", "Anna")
        {
            Anrede = Anrede.Frau,
            Titel = "Dr.",
            Email = "anna@example.de",
            Telefon = "0221 123",
            Mobil = "0170 123",
            Geburtsdatum = new DateOnly(1990, 1, 1),
            Notizen = "Hund",
            Firma = "Schmidt GmbH",
            Taetigkeit = "IT",
            BenutzerId = Guid.CreateVersion7(),
        };
        mieter.AnschriftSetzen(new DateOnly(2026, 1, 1), "Lindenweg 3", "50667", "Köln");
        mieter.BankverbindungHinzufuegen("DE02120300000000202051");
        var zeitpunkt = new DateTimeOffset(2037, 1, 1, 0, 0, 0, TimeSpan.Zero);

        mieter.Anonymisieren(zeitpunkt);

        Assert.True(mieter.IstAnonymisiert);
        Assert.Equal(zeitpunkt, mieter.AnonymisiertAm);
        Assert.StartsWith("Anonymisiert ", mieter.Nachname, StringComparison.Ordinal);
        Assert.DoesNotContain("Schmidt", mieter.Anzeigename, StringComparison.Ordinal);
        Assert.All(new object?[] { mieter.Vorname, mieter.Titel, mieter.Email, mieter.Telefon, mieter.Mobil, mieter.Geburtsdatum, mieter.Notizen, mieter.Firma, mieter.Taetigkeit, mieter.BenutzerId }, Assert.Null);
        Assert.Empty(mieter.Anschriften);
        Assert.Empty(mieter.Bankverbindungen);
        Assert.Throws<RegelverletzungException>(() => mieter.AnschriftSetzen(new DateOnly(2037, 2, 1), "X", "1", "Y"));
    }
}
