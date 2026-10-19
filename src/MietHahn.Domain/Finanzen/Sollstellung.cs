using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Vertraege;

namespace MietHahn.Domain.Finanzen;

public enum Postenart
{
    /// <summary>Monatliche Miete aus den Mietbestandteilen (automatisch).</summary>
    Miete = 0,
    Nachzahlung = 1,

    /// <summary>Guthaben des Mieters, z. B. aus der Nebenkostenabrechnung (negativer Betrag).</summary>
    Erstattung = 2,
    Mahngebuehr = 3,
    Ruecklastschriftgebuehr = 4,
    Verzugszinsen = 5,
    Schadensersatz = 6,
    Kaution = 7,

    /// <summary>Offener Betrag aus der Zeit vor dem Buchhaltungsbeginn.</summary>
    Saldovortrag = 8,
    Sonstiges = 99,
}

/// <summary>Rang bei der Verrechnung nach § 367 BGB.</summary>
public enum Verrechnungsrang
{
    Kosten = 0,
    Zinsen = 1,
    Hauptforderung = 2,
}

/// <summary>
/// Eine Forderung an den Mieter (Soll) bzw. bei negativem Betrag eine Gutschrift (Haben).
/// Monatsmieten erzeugt der Job je Bestandteil und Leistungsmonat; alles andere wird von Hand angelegt.
/// </summary>
public sealed class Sollstellung : Entitaet
{
    private Sollstellung()
    {
        Bezeichnung = "";
    }

    public Sollstellung(Guid mietvertragId, Postenart art, DateOnly faellig, decimal betrag, string bezeichnung)
    {
        if (art == Postenart.Miete)
        {
            throw new ArgumentException("Monatsmieten entstehen über Monatsmiete().", nameof(art));
        }

        MietvertragId = mietvertragId;
        Art = art;
        Faellig = faellig;
        Betrag = betrag;
        Bezeichnung = bezeichnung;
        Pruefen();
    }

    public Guid MietvertragId { get; private set; }

    public Postenart Art { get; private set; }

    /// <summary>Nur bei Monatsmieten: welcher Bestandteil.</summary>
    public Bestandteilart? Bestandteil { get; private set; }

    /// <summary>Nur bei Monatsmieten: erster Tag des Monats, für den die Miete geschuldet ist.</summary>
    public DateOnly? Leistungsmonat { get; private set; }

    public DateOnly Faellig { get; private set; }

    public decimal Betrag { get; private set; }

    public string Bezeichnung { get; private set; }

    public string? Notiz { get; set; }

    public bool Automatisch => Art == Postenart.Miete;

    public Verrechnungsrang Rang => Art switch
    {
        Postenart.Mahngebuehr or Postenart.Ruecklastschriftgebuehr => Verrechnungsrang.Kosten,
        Postenart.Verzugszinsen => Verrechnungsrang.Zinsen,
        _ => Verrechnungsrang.Hauptforderung,
    };

    public static Sollstellung Monatsmiete(Guid mietvertragId, DateOnly leistungsmonat, Bestandteilart bestandteil, DateOnly faellig, decimal betrag, string bezeichnung)
    {
        if (leistungsmonat.Day != 1)
        {
            throw new ArgumentException("Der Leistungsmonat beginnt am Ersten.", nameof(leistungsmonat));
        }

        var sollstellung = new Sollstellung
        {
            MietvertragId = mietvertragId,
            Art = Postenart.Miete,
            Bestandteil = bestandteil,
            Leistungsmonat = leistungsmonat,
            Faellig = faellig,
            Betrag = betrag,
            Bezeichnung = bezeichnung,
        };
        sollstellung.Pruefen();
        return sollstellung;
    }

    /// <summary>Gleicht eine automatische Monatsmiete an geänderte Vertragsdaten an.</summary>
    public bool Angleichen(DateOnly faellig, decimal betrag, string bezeichnung)
    {
        if (!Automatisch)
        {
            throw new InvalidOperationException("Nur automatische Monatsmieten werden angeglichen.");
        }

        if (Faellig == faellig && Betrag == betrag && Bezeichnung == bezeichnung)
        {
            return false;
        }

        Faellig = faellig;
        Betrag = betrag;
        Bezeichnung = bezeichnung;
        return true;
    }

    public void Aendern(Postenart art, DateOnly faellig, decimal betrag, string bezeichnung)
    {
        if (Automatisch || art == Postenart.Miete)
        {
            throw new RegelverletzungException("Monatsmieten ergeben sich aus dem Vertrag; Korrekturen bitte als eigene Sollstellung.");
        }

        Art = art;
        Faellig = faellig;
        Betrag = betrag;
        Bezeichnung = bezeichnung;
        Pruefen();
    }

    private void Pruefen()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Bezeichnung);
        if (Betrag == 0)
        {
            throw new RegelverletzungException("Der Betrag darf nicht 0 sein.");
        }

        if (decimal.Round(Betrag, 2) != Betrag)
        {
            throw new RegelverletzungException("Beträge haben höchstens zwei Nachkommastellen.");
        }

        if (Art == Postenart.Erstattung && Betrag > 0)
        {
            throw new RegelverletzungException("Eine Erstattung ist ein Guthaben des Mieters und wird negativ erfasst.");
        }
    }
}

public enum Zahlungsherkunft
{
    Manuell = 0,
    Bank = 1,
}

/// <summary>
/// Zahlung des Mieters (positiv) bzw. Auszahlung an den Mieter (negativ, z. B. ausgezahltes Guthaben).
/// </summary>
public sealed class Zahlung : Entitaet
{
    private Zahlung()
    {
    }

    public Zahlung(Guid mietvertragId, DateOnly datum, decimal betrag, Zahlungsherkunft herkunft = Zahlungsherkunft.Manuell)
    {
        MietvertragId = mietvertragId;
        Datum = datum;
        Betrag = betrag;
        Herkunft = herkunft;
        Pruefen();
    }

    public Guid MietvertragId { get; private set; }

    /// <summary>Tag des Zahlungseingangs (Wertstellung), maßgeblich für das Zuflussprinzip.</summary>
    public DateOnly Datum { get; private set; }

    public decimal Betrag { get; private set; }

    public Zahlungsherkunft Herkunft { get; private set; }

    public string? Verwendungszweck { get; set; }

    /// <summary>Tilgungsbestimmung des Mieters (§ 366 Abs. 1 BGB): diese Forderung zuerst.</summary>
    public Guid? SollstellungId { get; set; }

    public void Aendern(DateOnly datum, decimal betrag)
    {
        Datum = datum;
        Betrag = betrag;
        Pruefen();
    }

    private void Pruefen()
    {
        if (Betrag == 0 || decimal.Round(Betrag, 2) != Betrag)
        {
            throw new RegelverletzungException("Bitte einen Betrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }
    }
}

public enum Verrechnungsreihenfolge
{
    /// <summary>§§ 366, 367 BGB: Kosten → Zinsen → älteste Forderung.</summary>
    Gesetzlich = 0,

    /// <summary>Nur nach Fälligkeit, Kosten und Zinsen ohne Vorrang.</summary>
    AeltesteZuerst = 1,

    /// <summary>Erst Monatsmieten (älteste zuerst), dann alle übrigen Forderungen.</summary>
    MieteZuerst = 2,
}

/// <summary>Einstellungen der Buchhaltung (genau ein Datensatz).</summary>
public sealed class Finanzeinstellungen : Entitaet
{
    /// <summary>Ab diesem Monat erzeugt der Job Monatsmieten; leer = noch keine automatischen Sollstellungen.</summary>
    public DateOnly? BuchhaltungAb { get; private set; }

    public Verrechnungsreihenfolge Verrechnung { get; set; }

    public void BuchhaltungAbSetzen(DateOnly? ab) =>
        BuchhaltungAb = ab is { } tag ? new DateOnly(tag.Year, tag.Month, 1) : null;
}
