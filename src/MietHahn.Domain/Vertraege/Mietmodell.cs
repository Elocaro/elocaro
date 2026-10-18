using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Vertraege;

public enum Mietmodell
{
    /// <summary>Erhöhung auf die ortsübliche Vergleichsmiete (§ 558 BGB).</summary>
    Standard = 0,

    /// <summary>Staffelmiete (§ 557a BGB): vereinbarte Erhöhungen zu festen Terminen.</summary>
    Staffel = 1,

    /// <summary>Indexmiete (§ 557b BGB): Anpassung an den Verbraucherpreisindex.</summary>
    Index = 2,
}

/// <summary>Eine vereinbarte Staffel: ab diesem Tag gilt die neue Kaltmiete.</summary>
public sealed class Mietstaffel : Entitaet
{
    private Mietstaffel()
    {
    }

    internal Mietstaffel(Guid mietvertragId, DateOnly ab, decimal kaltmiete)
    {
        MietvertragId = mietvertragId;
        Ab = ab;
        Kaltmiete = kaltmiete;
    }

    public Guid MietvertragId { get; private set; }

    public DateOnly Ab { get; private set; }

    /// <summary>Neue Kaltmiete in Euro (§ 557a: als Geldbetrag ausgewiesen).</summary>
    public decimal Kaltmiete { get; private set; }

    /// <summary>Wann die Staffel als Mietbestandteil übernommen wurde.</summary>
    public DateTimeOffset? AngewendetAm { get; private set; }

    internal void Angewendet(DateTimeOffset zeitpunkt) => AngewendetAm = zeitpunkt;
}

/// <summary>Monatlicher Verbraucherpreisindex (Destatis, Basis 2020 = 100, Tabelle 61111-0002).</summary>
public sealed class VpiWert : Entitaet
{
    private VpiWert()
    {
        Quelle = "";
    }

    public VpiWert(DateOnly monat, decimal wert, string quelle = "manuell")
    {
        if (wert is <= 0 or > 1000)
        {
            throw new RegelverletzungException("Der Indexwert ist unplausibel.");
        }

        Monat = new DateOnly(monat.Year, monat.Month, 1);
        Wert = decimal.Round(wert, 1);
        Quelle = quelle;
    }

    /// <summary>Erster Tag des Monats.</summary>
    public DateOnly Monat { get; private set; }

    /// <summary>Indexstand mit einer Nachkommastelle, z. B. 121,7.</summary>
    public decimal Wert { get; private set; }

    public string Quelle { get; private set; }

    public void Aendern(decimal wert, string quelle)
    {
        if (wert is <= 0 or > 1000)
        {
            throw new RegelverletzungException("Der Indexwert ist unplausibel.");
        }

        Wert = decimal.Round(wert, 1);
        Quelle = quelle;
    }
}

/// <summary>Rechenregeln der Indexmiete (§ 557b BGB).</summary>
public static class Indexmiete
{
    /// <summary>Die Miete muss jeweils mindestens ein Jahr unverändert bleiben (§ 557b Abs. 2 BGB).</summary>
    public const int SperrfristMonate = 12;

    /// <summary>Neue Kaltmiete = bisherige × (VPI neu ÷ VPI alt), kaufmännisch auf Cent gerundet.</summary>
    public static decimal NeueMiete(decimal bisherigeMiete, decimal vpiAlt, decimal vpiNeu)
    {
        if (vpiAlt <= 0 || vpiNeu <= 0)
        {
            throw new RegelverletzungException("Indexwerte müssen größer als 0 sein.");
        }

        return Math.Round(bisherigeMiete * vpiNeu / vpiAlt, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Veränderung in Prozent mit einer Nachkommastelle (z. B. für das Anpassungsschreiben).</summary>
    public static decimal VeraenderungProzent(decimal vpiAlt, decimal vpiNeu) =>
        Math.Round(((vpiNeu / vpiAlt) - 1) * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Frühester Tag, ab dem die angepasste Miete gelten darf (ein Jahr nach der letzten Änderung).</summary>
    public static DateOnly FruehestesWirksamwerden(DateOnly letzteAenderung) => letzteAenderung.AddMonths(SperrfristMonate);

    /// <summary>Die geänderte Miete gilt ab dem übernächsten Monat nach Zugang der Erklärung (§ 557b Abs. 3 BGB).</summary>
    public static DateOnly WirksamAb(DateOnly erklaerungZugegangen) =>
        new DateOnly(erklaerungZugegangen.Year, erklaerungZugegangen.Month, 1).AddMonths(2);
}
