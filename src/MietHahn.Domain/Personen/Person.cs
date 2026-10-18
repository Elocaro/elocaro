using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Personen;

public enum Anrede
{
    Keine = 0,
    Frau = 1,
    Herr = 2,
    Divers = 3,
}

/// <summary>Kontakte mit besonderer Funktion. Mieter und Bürgen ergeben sich aus den Verträgen.</summary>
public enum Kontaktart
{
    Keine = 0,
    Hausmeister = 1,
    Handwerker = 2,
    Versorger = 3,
    Versicherung = 4,
    Behoerde = 5,
    Dienstleister = 6,
    Sonstige = 9,
}

/// <summary>
/// Natürliche Person oder Firma: Mieter, Bürge, Hausmeister, Handwerker, Versorger … Anschriften sind zeitabhängig
/// (Voranschrift, aktuelle Anschrift, neue Anschrift nach Auszug für die Zustellung der Abrechnung).
/// </summary>
public sealed class Person : Entitaet
{
    private Person()
    {
        Nachname = "";
    }

    public Person(string nachname, string? vorname = null)
    {
        Nachname = nachname;
        Vorname = vorname;
        Pruefen();
    }

    public Anrede Anrede { get; set; }

    public string? Titel { get; set; }

    public string? Vorname { get; set; }

    /// <summary>Nachname bzw. bei Firmen der Firmenname.</summary>
    public string Nachname { get; set; }

    /// <summary>Firma (bei Kontakten) bzw. Zusatz, z. B. „Stadtwerke Köln GmbH“.</summary>
    public string? Firma { get; set; }

    public Kontaktart Kontaktart { get; set; }

    /// <summary>Gewerk (Handwerker) oder Sparte (Versorger), z. B. „Sanitär“, „Wasser“.</summary>
    public string? Taetigkeit { get; set; }

    [Vertraulich]
    public DateOnly? Geburtsdatum { get; set; }

    public string? Email { get; set; }

    public string? Telefon { get; set; }

    public string? Mobil { get; set; }

    public string? Notizen { get; set; }

    /// <summary>Verknüpftes Benutzerkonto (Portal, Servicebereich).</summary>
    public Guid? BenutzerId { get; set; }

    /// <summary>Gesetzt, wenn die personenbezogenen Daten nach Ablauf der Aufbewahrungsfrist entfernt wurden.</summary>
    public DateTimeOffset? AnonymisiertAm { get; private set; }

    public List<PersonAnschrift> Anschriften { get; private set; } = [];

    public List<Bankverbindung> Bankverbindungen { get; private set; } = [];

    public bool IstAnonymisiert => AnonymisiertAm is not null;

    /// <summary>„Dr. Anna Schmidt“ bzw. „Stadtwerke Köln GmbH (Max Müller)“.</summary>
    public string Anzeigename
    {
        get
        {
            var name = string.Join(' ', new[] { Titel, Vorname, Nachname }.Where(t => !string.IsNullOrWhiteSpace(t)));
            return Firma is null ? name : $"{Firma} ({name})";
        }
    }

    /// <summary>Für sortierte Listen: „Schmidt, Anna“.</summary>
    public string Sortiername => Vorname is null ? Nachname : $"{Nachname}, {Vorname}";

    public void Pruefen()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Nachname);
        if (IstAnonymisiert)
        {
            throw new RegelverletzungException("Anonymisierte Personen können nicht mehr geändert werden.");
        }
    }

    /// <summary>Neue Anschrift ab Stichtag (Umzug); die bisherige endet am Vortag.</summary>
    public PersonAnschrift AnschriftSetzen(DateOnly ab, string strasse, string plz, string ort, string? zusatz = null, string land = "Deutschland")
    {
        Pruefen();
        return Zeitreihe.Fortschreiben(Anschriften, ab, g => new PersonAnschrift(Id, g, strasse, plz, ort, zusatz, land));
    }

    public PersonAnschrift? AnschriftAm(DateOnly tag) => Zeitreihe.Am(Anschriften, tag);

    public Bankverbindung BankverbindungHinzufuegen(string iban, string? kontoinhaber = null, string? bic = null)
    {
        Pruefen();
        if (!Iban.IstGueltig(iban))
        {
            throw new RegelverletzungException($"„{iban}“ ist keine gültige IBAN (Prüfsumme stimmt nicht).");
        }

        var formatiert = Iban.Formatieren(iban);
        if (Bankverbindungen.Any(b => b.Iban == formatiert))
        {
            throw new RegelverletzungException("Diese IBAN ist bereits hinterlegt.");
        }

        var neu = new Bankverbindung(Id, formatiert, string.IsNullOrWhiteSpace(kontoinhaber) ? null : kontoinhaber.Trim(), string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant());
        Bankverbindungen.Add(neu);
        return neu;
    }

    /// <summary>
    /// Entfernt alle personenbezogenen Daten (DSGVO, nach Ablauf der Aufbewahrungsfrist). Die Person bleibt als Datensatz
    /// erhalten, damit Buchungen und Verträge auswertbar bleiben.
    /// </summary>
    public void Anonymisieren(DateTimeOffset zeitpunkt)
    {
        if (IstAnonymisiert)
        {
            return;
        }

        Anrede = Anrede.Keine;
        Titel = null;
        Vorname = null;
        Nachname = $"Anonymisiert {Id.ToString()[..8]}";
        Firma = null;
        Taetigkeit = null;
        Geburtsdatum = null;
        Email = null;
        Telefon = null;
        Mobil = null;
        Notizen = null;
        BenutzerId = null;
        Anschriften.Clear();
        Bankverbindungen.Clear();
        AnonymisiertAm = zeitpunkt;
    }
}

public sealed class PersonAnschrift : Entitaet, IZeitabhaengig
{
    private PersonAnschrift()
    {
        Strasse = "";
        Plz = "";
        Ort = "";
        Land = "";
    }

    internal PersonAnschrift(Guid personId, Zeitraum gueltigkeit, string strasse, string plz, string ort, string? zusatz, string land)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strasse);
        ArgumentException.ThrowIfNullOrWhiteSpace(plz);
        ArgumentException.ThrowIfNullOrWhiteSpace(ort);
        PersonId = personId;
        Gueltigkeit = gueltigkeit;
        Strasse = strasse.Trim();
        Plz = plz.Trim();
        Ort = ort.Trim();
        Zusatz = string.IsNullOrWhiteSpace(zusatz) ? null : zusatz.Trim();
        Land = string.IsNullOrWhiteSpace(land) ? "Deutschland" : land.Trim();
    }

    public Guid PersonId { get; private set; }

    public Zeitraum Gueltigkeit { get; private set; }

    /// <summary>z. B. „c/o Müller“ oder „Wohnung 4“.</summary>
    public string? Zusatz { get; private set; }

    public string Strasse { get; private set; }

    public string Plz { get; private set; }

    public string Ort { get; private set; }

    public string Land { get; private set; }

    public string Einzeilig => string.Join(", ", new[] { Zusatz, Strasse, $"{Plz} {Ort}", Land == "Deutschland" ? null : Land }.Where(t => t is not null));

    public void GueltigkeitBeenden(DateOnly letzterTag) => Gueltigkeit = Gueltigkeit.BeendenZum(letzterTag);
}

/// <summary>Konto einer Person: für Lastschrift/Erstattung und die automatische Zuordnung eingehender Zahlungen.</summary>
public sealed class Bankverbindung : Entitaet
{
    private Bankverbindung()
    {
        Iban = "";
    }

    internal Bankverbindung(Guid personId, string iban, string? kontoinhaber, string? bic)
    {
        PersonId = personId;
        Iban = iban;
        Kontoinhaber = kontoinhaber;
        Bic = bic;
    }

    public Guid PersonId { get; private set; }

    /// <summary>Formatiert in Vierergruppen.</summary>
    [Vertraulich]
    public string Iban { get; private set; }

    /// <summary>Falls abweichend, z. B. Eltern zahlen die Miete.</summary>
    public string? Kontoinhaber { get; private set; }

    public string? Bic { get; private set; }
}
