using MietHahn.Domain.Objekte;
using MietHahn.Domain.Personen;

namespace MietHahn.Web.Anzeige;

/// <summary>Deutsche Anzeigenamen für fachliche Aufzählungen.</summary>
public static class Bezeichnungen
{
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
}
