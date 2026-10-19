using MietHahn.Domain.Dokumente;
using MietHahn.Domain.Finanzen;
using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;
using MietHahn.Domain.Vertraege;
using MietHahn.Domain.Zaehlerwesen;

namespace MietHahn.Web.Anzeige;

/// <summary>Deutsche Anzeigenamen für fachliche Aufzählungen.</summary>
public static class Bezeichnungen
{
    public static string Von(Postenart wert) => wert switch
    {
        Postenart.Miete => "Miete",
        Postenart.Nachzahlung => "Nachzahlung (Abrechnung)",
        Postenart.Erstattung => "Erstattung / Gutschrift",
        Postenart.Mahngebuehr => "Mahngebühr",
        Postenart.Ruecklastschriftgebuehr => "Rücklastschriftgebühr",
        Postenart.Verzugszinsen => "Verzugszinsen",
        Postenart.Schadensersatz => "Schadensersatz",
        Postenart.Kaution => "Kaution",
        Postenart.Saldovortrag => "Saldovortrag",
        _ => "Sonstige Forderung",
    };

    public static string Von(Verrechnungsreihenfolge wert) => wert switch
    {
        Verrechnungsreihenfolge.AeltesteZuerst => "Nur nach Fälligkeit (älteste zuerst)",
        Verrechnungsreihenfolge.MieteZuerst => "Erst Mieten, dann übrige Forderungen",
        _ => "Gesetzlich (§§ 366, 367 BGB): Kosten → Zinsen → älteste Forderung",
    };

    public static string Von(Dokumentkategorie wert) => wert switch
    {
        Dokumentkategorie.Vertrag => "Vertrag",
        Dokumentkategorie.Protokoll => "Protokoll",
        Dokumentkategorie.Abrechnung => "Abrechnung",
        Dokumentkategorie.Korrespondenz => "Korrespondenz",
        Dokumentkategorie.Rechnung => "Rechnung",
        Dokumentkategorie.Versicherung => "Versicherung",
        Dokumentkategorie.Energieausweis => "Energieausweis",
        Dokumentkategorie.Grundriss => "Grundriss",
        Dokumentkategorie.Foto => "Foto",
        Dokumentkategorie.Hausordnung => "Hausordnung",
        _ => "Sonstiges",
    };

    public static string Von(Heizungsart wert) => wert switch
    {
        Heizungsart.Zentralheizung => "Zentralheizung",
        Heizungsart.Etagenheizung => "Etagenheizung",
        Heizungsart.Fernwaerme => "Fernwärme",
        Heizungsart.Waermepumpe => "Wärmepumpe",
        Heizungsart.Einzeloefen => "Einzelöfen",
        Heizungsart.Sonstige => "Sonstige",
        _ => "unbekannt",
    };

    public static string Von(Energietraeger wert) => wert switch
    {
        Energietraeger.Erdgas => "Erdgas",
        Energietraeger.Heizoel => "Heizöl",
        Energietraeger.Fernwaerme => "Fernwärme",
        Energietraeger.Strom => "Strom",
        Energietraeger.Holz => "Holz/Pellets",
        Energietraeger.Fluessiggas => "Flüssiggas",
        Energietraeger.Sonstige => "Sonstige",
        _ => "unbekannt",
    };

    public static string Von(Einheitstyp wert) => wert switch
    {
        Einheitstyp.Wohnung => "Wohnung",
        Einheitstyp.Gewerbe => "Gewerbe",
        Einheitstyp.Stellplatz => "Stellplatz",
        _ => "Sonstiges",
    };

    public static string Von(Moeblierung wert) => wert switch
    {
        Moeblierung.Teilmoebliert => "teilmöbliert",
        Moeblierung.Moebliert => "möbliert",
        _ => "unmöbliert",
    };

    public static string Von(Anrede wert) => wert switch
    {
        Anrede.Frau => "Frau",
        Anrede.Herr => "Herr",
        Anrede.Divers => "divers",
        _ => "–",
    };

    public static string Von(Kontaktart wert) => wert switch
    {
        Kontaktart.Hausmeister => "Hausmeister",
        Kontaktart.Handwerker => "Handwerker",
        Kontaktart.Versorger => "Versorger",
        Kontaktart.Versicherung => "Versicherung",
        Kontaktart.Behoerde => "Behörde",
        Kontaktart.Dienstleister => "Dienstleister",
        Kontaktart.Sonstige => "Sonstiger Kontakt",
        _ => "privat (Mieter, Bürge …)",
    };

    public static string Von(Bestandteilart wert) => wert switch
    {
        Bestandteilart.Kaltmiete => "Kaltmiete",
        Bestandteilart.BetriebskostenVorauszahlung => "Betriebskosten-Vorauszahlung",
        Bestandteilart.HeizkostenVorauszahlung => "Heizkosten-Vorauszahlung",
        Bestandteilart.Betriebskostenpauschale => "Betriebskostenpauschale",
        Bestandteilart.Stellplatz => "Stellplatz",
        Bestandteilart.Moeblierungszuschlag => "Möblierungszuschlag",
        _ => "Sonstiges",
    };

    public static string Von(Abrechnungsart wert) => wert switch
    {
        Abrechnungsart.Pauschale => "Pauschale (keine Abrechnung)",
        Abrechnungsart.Inklusiv => "Inklusivmiete",
        _ => "Vorauszahlung mit Abrechnung",
    };

    public static string Von(Zahlungsart wert) => wert == Zahlungsart.Lastschrift ? "Lastschrift" : "Überweisung";

    public static string Von(Kuendigender wert) => wert switch
    {
        Kuendigender.Vermieter => "Vermieter",
        Kuendigender.Einvernehmlich => "einvernehmlich",
        _ => "Mieter",
    };

    public static string Von(Parteirolle wert) => wert == Parteirolle.Buerge ? "Bürge" : "Mieter";

    public static string Von(Vertragsstatus wert) => wert switch
    {
        Vertragsstatus.Kuenftig => "beginnt künftig",
        Vertragsstatus.Gekuendigt => "gekündigt/befristet",
        Vertragsstatus.Beendet => "beendet",
        _ => "läuft",
    };

    public static string StatusKlasse(Vertragsstatus wert) => wert switch
    {
        Vertragsstatus.Aktiv => "status-ok",
        Vertragsstatus.Gekuendigt or Vertragsstatus.Kuenftig => "status-wartend",
        _ => "",
    };

    public static string Von(Mietmodell wert) => wert switch
    {
        Mietmodell.Staffel => "Staffelmiete",
        Mietmodell.Index => "Indexmiete",
        _ => "Standardmiete",
    };

    public static string Von(Zaehlertyp wert) => wert switch
    {
        Zaehlertyp.Warmwasser => "Warmwasser",
        Zaehlertyp.Waerme => "Wärme",
        Zaehlertyp.Strom => "Strom",
        Zaehlertyp.Gas => "Gas",
        Zaehlertyp.Oeltank => "Öltank",
        _ => "Kaltwasser",
    };

    public static string Von(Messeinheit wert) => wert switch
    {
        Messeinheit.Kilowattstunden => "kWh",
        Messeinheit.Megawattstunden => "MWh",
        Messeinheit.Liter => "l",
        _ => "m³",
    };

    public static string Von(Standart wert) => wert switch
    {
        Standart.Schaetzung => "Schätzung",
        Standart.Zwischenablesung => "Zwischenablesung",
        Standart.Einbau => "Einbau",
        Standart.Ausbau => "Ausbau",
        _ => "Ablesung",
    };
}
