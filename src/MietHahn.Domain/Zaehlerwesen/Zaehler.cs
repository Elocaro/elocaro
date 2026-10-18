using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Zaehlerwesen;

public enum Zaehlertyp
{
    Kaltwasser = 0,
    Warmwasser = 1,
    Waerme = 2,
    Strom = 3,
    Gas = 4,
    Oeltank = 5,
}

public enum Messeinheit
{
    Kubikmeter = 0,
    Kilowattstunden = 1,
    Megawattstunden = 2,
    Liter = 3,
}

public enum Standart
{
    Ablesung = 0,
    Schaetzung = 1,

    /// <summary>Zusätzliche Ablesung, z. B. bei Mieterwechsel.</summary>
    Zwischenablesung = 2,

    /// <summary>Anfangsstand beim Einbau.</summary>
    Einbau = 3,

    /// <summary>Endstand beim Ausbau (Zählertausch).</summary>
    Ausbau = 4,
}

/// <summary>
/// Physischer Zähler. Beim Tausch wird ein neuer Zähler mit Verweis auf den alten (<see cref="VorgaengerId"/>) angelegt;
/// zusammen bilden sie eine Messstelle, über die der Verbrauch durchgängig berechnet wird.
/// </summary>
public sealed class Zaehler : Entitaet
{
    private Zaehler()
    {
        Bezeichnung = "";
        Nummer = "";
    }

    public Zaehler(Guid objektId, Zaehlertyp typ, string bezeichnung, string nummer, DateOnly eingebautAm, decimal anfangsstand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bezeichnung);
        ArgumentException.ThrowIfNullOrWhiteSpace(nummer);
        ObjektId = objektId;
        Typ = typ;
        Messeinheit = StandardEinheit(typ);
        Bezeichnung = bezeichnung.Trim();
        Nummer = nummer.Trim();
        EingebautAm = eingebautAm;
        EichfristBis = StandardEichfrist(typ, eingebautAm);
        Staende.Add(new Zaehlerstand(Id, eingebautAm, Pruefe(anfangsstand), Standart.Einbau, null, null, null));
    }

    public Guid ObjektId { get; private set; }

    /// <summary>Zugeordnete Einheit (Wohnungszähler); <c>null</c> = Hauszähler (Haupt-, Allgemeinzähler).</summary>
    public Guid? EinheitId { get; private set; }

    public Guid? ZimmerId { get; private set; }

    public Zaehlertyp Typ { get; private set; }

    public Messeinheit Messeinheit { get; set; }

    /// <summary>z. B. „Kaltwasser Bad“ oder „Hauptwasserzähler“.</summary>
    public string Bezeichnung { get; set; }

    public string Nummer { get; set; }

    /// <summary>Wo der Zähler hängt, z. B. „Keller, Raum 2“.</summary>
    public string? Lage { get; set; }

    /// <summary>Übergeordneter Zähler (z. B. Hauptwasserzähler); Differenz = Allgemeinverbrauch/Schwund.</summary>
    public Guid? HauptzaehlerId { get; set; }

    /// <summary>Alter Zähler derselben Messstelle (Zählertausch).</summary>
    public Guid? VorgaengerId { get; private set; }

    public DateOnly EingebautAm { get; private set; }

    public DateOnly? AusgebautAm { get; private set; }

    /// <summary>Ende der Eichgültigkeit (Wasser 6 J., Warmwasser/Wärme 5 J., Gas/Strom 8 J. ab Einbau).</summary>
    public DateOnly? EichfristBis { get; set; }

    /// <summary>Reihenfolge in der Ablese-Runde.</summary>
    public int Reihenfolge { get; set; }

    public string? Notizen { get; set; }

    public List<Zaehlerstand> Staende { get; private set; } = [];

    public bool IstAktivAm(DateOnly tag) => tag >= EingebautAm && (AusgebautAm is null || tag <= AusgebautAm);

    public void Zuordnen(Guid? einheitId, Guid? zimmerId)
    {
        if (zimmerId is not null && einheitId is null)
        {
            throw new RegelverletzungException("Ein Zimmerzähler gehört zu einer Einheit.");
        }

        EinheitId = einheitId;
        ZimmerId = zimmerId;
    }

    /// <summary>
    /// Erfasst einen Stand. Fehler (z. B. vor dem Einbau) werfen; Auffälligkeiten (Rückwärtslauf, Ausreißer) liefert
    /// <see cref="Pruefen"/> vorab, damit die Oberfläche nachfragen kann.
    /// </summary>
    public Zaehlerstand StandErfassen(DateOnly datum, decimal wert, Standart art, string? ableser, Guid? fotoDateiId = null, string? notiz = null)
    {
        if (art is Standart.Einbau or Standart.Ausbau)
        {
            throw new RegelverletzungException("Einbau- und Ausbaustände entstehen beim Ein- bzw. Ausbau des Zählers.");
        }

        if (!IstAktivAm(datum))
        {
            throw new RegelverletzungException($"Der Zähler war am {datum:dd.MM.yyyy} nicht eingebaut ({EingebautAm:dd.MM.yyyy} – {(AusgebautAm is { } aus ? aus.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) : "heute")}).");
        }

        if (Staende.Any(s => s.Datum == datum))
        {
            throw new RegelverletzungException($"Für den {datum:dd.MM.yyyy} gibt es bereits einen Stand.");
        }

        var stand = new Zaehlerstand(Id, datum, Pruefe(wert), art, ableser, fotoDateiId, notiz);
        Staende.Add(stand);
        return stand;
    }

    public void StandEntfernen(Guid standId)
    {
        var stand = Staende.SingleOrDefault(s => s.Id == standId) ?? throw new RegelverletzungException("Der Stand wurde nicht gefunden.");
        if (stand.Art is Standart.Einbau or Standart.Ausbau)
        {
            throw new RegelverletzungException("Einbau- und Ausbaustände lassen sich nicht entfernen.");
        }

        Staende.Remove(stand);
    }

    /// <summary>Plausibilitätsprüfung eines neuen Werts gegen die vorhandenen Stände.</summary>
    /// <param name="ausreisserProzent">Ab welcher Abweichung des Tagesverbrauchs vom bisherigen Durchschnitt gewarnt wird.</param>
    public IReadOnlyList<string> Pruefen(DateOnly datum, decimal wert, int ausreisserProzent = 50)
    {
        var warnungen = new List<string>();
        var vorher = Staende.Where(s => s.Datum < datum).MaxBy(s => s.Datum);
        var nachher = Staende.Where(s => s.Datum > datum).MinBy(s => s.Datum);
        if (vorher is not null && wert < vorher.Wert)
        {
            warnungen.Add(Text($"Rückwärtslauf: Der Wert ist kleiner als der Stand vom {vorher.Datum:dd.MM.yyyy} ({vorher.Wert:#,##0.###}). Zählertausch?"));
        }

        if (nachher is not null && wert > nachher.Wert)
        {
            warnungen.Add(Text($"Der Wert ist größer als der spätere Stand vom {nachher.Datum:dd.MM.yyyy} ({nachher.Wert:#,##0.###})."));
        }

        // Ausreißer: Tagesverbrauch seit der letzten Ablesung im Vergleich zum Durchschnitt davor
        var erste = Staende.Where(s => s.Datum < datum).MinBy(s => s.Datum);
        if (vorher is not null && erste is not null && vorher != erste && wert >= vorher.Wert && datum > vorher.Datum)
        {
            var bisher = (vorher.Wert - erste.Wert) / (vorher.Datum.DayNumber - erste.Datum.DayNumber);
            var aktuell = (wert - vorher.Wert) / (datum.DayNumber - vorher.Datum.DayNumber);
            if (bisher > 0 && Math.Abs(aktuell - bisher) / bisher * 100 > ausreisserProzent)
            {
                warnungen.Add(Text($"Ungewöhnlicher Verbrauch: {aktuell:#,##0.###} pro Tag statt bisher {bisher:#,##0.###} ({(aktuell > bisher ? "+" : "")}{(aktuell - bisher) / bisher * 100:0} %)."));
            }
        }

        return warnungen;
    }

    private static string Text(FormattableString text) => text.ToString(System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Baut den Zähler aus (Endstand) und liefert den neuen Zähler der Messstelle mit Anfangsstand.</summary>
    public Zaehler Tauschen(DateOnly datum, decimal endstand, string neueNummer, decimal anfangsstand, DateOnly? eichfristBis = null)
    {
        if (AusgebautAm is not null)
        {
            throw new RegelverletzungException("Der Zähler ist bereits ausgebaut.");
        }

        if (datum < EingebautAm || Staende.Any(s => s.Datum > datum))
        {
            throw new RegelverletzungException("Nach dem Tauschdatum gibt es noch Stände dieses Zählers.");
        }

        var letzter = Staende.MaxBy(s => s.Datum)!;
        if (endstand < letzter.Wert)
        {
            throw new RegelverletzungException($"Der Endstand ist kleiner als der letzte Stand ({letzter.Wert:0.###}).");
        }

        if (letzter.Datum == datum)
        {
            Staende.Remove(letzter);
        }

        Staende.Add(new Zaehlerstand(Id, datum, Pruefe(endstand), Standart.Ausbau, null, null, null));
        AusgebautAm = datum;

        var neu = new Zaehler(ObjektId, Typ, Bezeichnung, neueNummer, datum, anfangsstand)
        {
            Messeinheit = Messeinheit,
            Lage = Lage,
            HauptzaehlerId = HauptzaehlerId,
            Reihenfolge = Reihenfolge,
            EichfristBis = eichfristBis ?? StandardEichfrist(Typ, datum),
            VorgaengerId = Id,
        };
        neu.Zuordnen(EinheitId, ZimmerId);
        return neu;
    }

    public static Messeinheit StandardEinheit(Zaehlertyp typ) => typ switch
    {
        Zaehlertyp.Waerme or Zaehlertyp.Strom => Messeinheit.Kilowattstunden,
        Zaehlertyp.Oeltank => Messeinheit.Liter,
        _ => Messeinheit.Kubikmeter, // Wasser, Gas
    };

    /// <summary>Eichfristen nach Anlage 7 MessEV (Kaltwasser 6, Warmwasser und Wärme 5, Gas und Strom 8 Jahre).</summary>
    public static DateOnly? StandardEichfrist(Zaehlertyp typ, DateOnly eingebautAm) => typ switch
    {
        Zaehlertyp.Kaltwasser => new DateOnly(eingebautAm.Year + 6, 12, 31),
        Zaehlertyp.Warmwasser or Zaehlertyp.Waerme => new DateOnly(eingebautAm.Year + 5, 12, 31),
        Zaehlertyp.Gas or Zaehlertyp.Strom => new DateOnly(eingebautAm.Year + 8, 12, 31),
        _ => null,
    };

    private static decimal Pruefe(decimal wert) =>
        wert < 0 ? throw new RegelverletzungException("Zählerstände sind nicht negativ.")
        : decimal.Round(wert, 3) != wert ? throw new RegelverletzungException("Zählerstände haben höchstens drei Nachkommastellen.")
        : wert;
}

public sealed class Zaehlerstand : Entitaet
{
    private Zaehlerstand()
    {
    }

    internal Zaehlerstand(Guid zaehlerId, DateOnly datum, decimal wert, Standart art, string? ableser, Guid? fotoDateiId, string? notiz)
    {
        ZaehlerId = zaehlerId;
        Datum = datum;
        Wert = wert;
        Art = art;
        Ableser = ableser;
        FotoDateiId = fotoDateiId;
        Notiz = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();
    }

    public Guid ZaehlerId { get; private set; }

    public DateOnly Datum { get; private set; }

    public decimal Wert { get; private set; }

    public Standart Art { get; private set; }

    public string? Ableser { get; private set; }

    /// <summary>Foto des Zählerstands als Nachweis.</summary>
    public Guid? FotoDateiId { get; private set; }

    public string? Notiz { get; private set; }
}
