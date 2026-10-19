using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Belege;

/// <summary>Anteil nach § 35a EStG (Steuerermäßigung des Mieters, Ausweis in der Nebenkostenabrechnung).</summary>
public enum Paragraf35a
{
    Keine = 0,
    HaushaltsnaheDienstleistung = 1,
    Handwerkerleistung = 2,
}

/// <summary>
/// Eingangsrechnung bzw. Kostenbeleg. Die Dateien liegen unveränderlich in der Dateiablage (SHA-256); Datenänderungen
/// hält das Änderungsprotokoll fest, gelöscht wird nur logisch.
/// </summary>
public sealed class Beleg : Entitaet
{
    private Beleg()
    {
    }

    /// <summary>Lieferant und übrige Angaben danach setzen und <see cref="Pruefen"/> aufrufen.</summary>
    public Beleg(DateOnly rechnungsdatum, decimal brutto)
    {
        Rechnungsdatum = rechnungsdatum;
        Brutto = brutto;
    }

    /// <summary>Lieferant aus den Kontakten; alternativ nur als Name.</summary>
    public Guid? LieferantId { get; set; }

    public string? LieferantName { get; set; }

    public string? Rechnungsnummer { get; set; }

    public DateOnly Rechnungsdatum { get; set; }

    public DateOnly? LeistungVon { get; private set; }

    public DateOnly? LeistungBis { get; private set; }

    /// <summary>Als zwei Spalten gespeichert (optionaler Zeitraum).</summary>
    public Zeitraum? Leistungszeitraum => LeistungVon is { } von ? new Zeitraum(von, LeistungBis) : null;

    public decimal Brutto { get; set; }

    public decimal? Netto { get; set; }

    public decimal? Umsatzsteuer { get; set; }

    public DateOnly? FaelligAm { get; set; }

    /// <summary>Von Hand erfasstes Zahlungsdatum (z. B. bar oder Lastschrift ohne Bankimport).</summary>
    public DateOnly? BezahltAm { get; set; }

    /// <summary>Brennstoffe: gelieferte Menge, Einheit (l, kWh, kg, m³) und CO₂-Emissionen laut Lieferant (CO2KostAufG).</summary>
    public decimal? Brennstoffmenge { get; set; }

    public string? Brennstoffeinheit { get; set; }

    public decimal? Co2Kg { get; set; }

    public string? Notiz { get; set; }

    public List<BelegDatei> Dateien { get; private set; } = [];

    public List<Kostenposition> Positionen { get; private set; } = [];

    public decimal Aufgeteilt => Positionen.Sum(p => p.Betrag);

    public bool IstVollstaendigAufgeteilt => Positionen.Count > 0 && Aufgeteilt == Brutto;

    public void LeistungszeitraumSetzen(DateOnly? von, DateOnly? bis)
    {
        if (von is null && bis is not null)
        {
            throw new RegelverletzungException("Bitte auch den Beginn des Leistungszeitraums angeben.");
        }

        if (bis < von)
        {
            throw new RegelverletzungException("Der Leistungszeitraum endet vor seinem Beginn.");
        }

        LeistungVon = von;
        LeistungBis = bis;
    }

    public void DateiHinzufuegen(Guid dateiId)
    {
        if (Dateien.All(d => d.DateiId != dateiId))
        {
            Dateien.Add(new BelegDatei(Id, dateiId, Dateien.Count == 0 ? 1 : Dateien.Max(d => d.Seite) + 1));
        }
    }

    public void DateiEntfernen(Guid dateiId) => Dateien.RemoveAll(d => d.DateiId == dateiId);

    public Kostenposition PositionHinzufuegen(string kostenart, decimal betrag, Guid objektId, Zeitraum abrechnungszeitraum, bool? umlagefaehig = null)
    {
        var art = Kostenarten.Finden(kostenart) ?? throw new RegelverletzungException("Unbekannte Kostenart.");
        if (betrag == 0 || decimal.Round(betrag, 2) != betrag)
        {
            throw new RegelverletzungException("Bitte einen Betrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }

        if (Math.Abs(Aufgeteilt + betrag) > Math.Abs(Brutto) || Math.Sign(betrag) != Math.Sign(Brutto))
        {
            throw new RegelverletzungException($"Die Positionen übersteigen den Rechnungsbetrag (offen: {Brutto - Aufgeteilt:0.00} €).");
        }

        var position = new Kostenposition(Id, art.Schluessel, betrag, objektId, abrechnungszeitraum, umlagefaehig ?? art.Umlagefaehig);
        Positionen.Add(position);
        return position;
    }

    public void PositionEntfernen(Guid positionId) => Positionen.RemoveAll(p => p.Id == positionId);

    /// <summary>Neuer Rechnungsbetrag: darf nicht unter der Summe der Positionen liegen.</summary>
    public void BruttoSetzen(decimal brutto)
    {
        if (Positionen.Count > 0 && (Math.Abs(Aufgeteilt) > Math.Abs(brutto) || Math.Sign(brutto) != Math.Sign(Brutto)))
        {
            throw new RegelverletzungException("Der Betrag ist kleiner als die Summe der Positionen; erst die Positionen anpassen.");
        }

        Brutto = brutto;
        Pruefen();
    }

    public void Pruefen()
    {
        if (Brutto == 0 || decimal.Round(Brutto, 2) != Brutto)
        {
            throw new RegelverletzungException("Bitte einen Rechnungsbetrag ungleich 0 mit höchstens zwei Nachkommastellen angeben.");
        }

        if (Netto is { } netto && Umsatzsteuer is { } ust && netto + ust != Brutto)
        {
            throw new RegelverletzungException($"Netto + Umsatzsteuer ({netto + ust:0.00} €) ergibt nicht den Bruttobetrag ({Brutto:0.00} €).");
        }

        if (string.IsNullOrWhiteSpace(LieferantName) && LieferantId is null)
        {
            throw new RegelverletzungException("Bitte den Lieferanten angeben.");
        }

        if (Co2Kg < 0 || Brennstoffmenge < 0)
        {
            throw new RegelverletzungException("Menge und CO₂ dürfen nicht negativ sein.");
        }
    }
}

/// <summary>Eine Seite bzw. Datei eines Belegs.</summary>
public sealed class BelegDatei : Entitaet
{
    private BelegDatei()
    {
    }

    internal BelegDatei(Guid belegId, Guid dateiId, int seite)
    {
        BelegId = belegId;
        DateiId = dateiId;
        Seite = seite;
    }

    public Guid BelegId { get; private set; }

    public Guid DateiId { get; private set; }

    public int Seite { get; private set; }
}

/// <summary>Teil eines Belegs für eine Kostenart, ein Objekt (optional eine Einheit) und einen Abrechnungszeitraum.</summary>
public sealed class Kostenposition : Entitaet
{
    private Kostenposition()
    {
        Kostenart = "";
    }

    internal Kostenposition(Guid belegId, string kostenart, decimal betrag, Guid objektId, Zeitraum abrechnungszeitraum, bool umlagefaehig)
    {
        if (abrechnungszeitraum.IstUnbefristet)
        {
            throw new RegelverletzungException("Der Abrechnungszeitraum braucht ein Ende.");
        }

        BelegId = belegId;
        Kostenart = kostenart;
        Betrag = betrag;
        ObjektId = objektId;
        Abrechnungszeitraum = abrechnungszeitraum;
        Umlagefaehig = umlagefaehig;
    }

    public Guid BelegId { get; private set; }

    /// <summary>Schlüssel aus <see cref="Kostenarten"/>.</summary>
    public string Kostenart { get; private set; }

    public decimal Betrag { get; private set; }

    public Guid ObjektId { get; private set; }

    /// <summary>Nur bei Kosten einer einzelnen Einheit (direkte Zuordnung).</summary>
    public Guid? EinheitId { get; set; }

    public Zeitraum Abrechnungszeitraum { get; private set; }

    public bool Umlagefaehig { get; set; }

    public Paragraf35a Paragraf35a { get; private set; }

    /// <summary>Lohn-, Maschinen- und Fahrtkosten inkl. USt; nur diese sind nach § 35a begünstigt.</summary>
    public decimal? Arbeitskosten { get; private set; }

    public string? Notiz { get; set; }

    public void Paragraf35aSetzen(Paragraf35a art, decimal? arbeitskosten)
    {
        if (art == Paragraf35a.Keine)
        {
            Paragraf35a = art;
            Arbeitskosten = null;
            return;
        }

        if (arbeitskosten is not { } kosten || kosten <= 0 || Math.Abs(kosten) > Math.Abs(Betrag))
        {
            throw new RegelverletzungException("Bitte den Arbeitskostenanteil angeben (höchstens der Positionsbetrag).");
        }

        Paragraf35a = art;
        Arbeitskosten = kosten;
    }
}
