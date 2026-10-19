using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Bank;

public enum Kontotyp
{
    Mietkonto = 0,
    Darlehen = 1,
    Ruecklage = 2,
    Sonstiges = 9,
}

/// <summary>Bankkonto des Vermieters, optional bestimmten Objekten zugeordnet.</summary>
public sealed class Bankkonto : Entitaet
{
    private Bankkonto()
    {
        Name = "";
        Iban = "";
    }

    public Bankkonto(string name, string iban, Kontotyp typ = Kontotyp.Mietkonto)
    {
        Name = name;
        Iban = "";
        IbanSetzen(iban);
        Typ = typ;
        Pruefen();
    }

    public string Name { get; set; }

    /// <summary>Kompakt ohne Leerzeichen, in Großbuchstaben.</summary>
    [Vertraulich]
    public string Iban { get; private set; }

    public string? Bic { get; set; }

    public string? Bank { get; set; }

    public Kontotyp Typ { get; set; }

    public List<Guid> ObjektIds { get; private set; } = [];

    public void IbanSetzen(string iban)
    {
        if (!Gemeinsam.Iban.IstGueltig(iban))
        {
            throw new RegelverletzungException("Die IBAN ist ungültig (Prüfsumme).");
        }

        Iban = Gemeinsam.Iban.Kompakt(iban);
    }

    public void ObjekteSetzen(IEnumerable<Guid> objektIds)
    {
        ArgumentNullException.ThrowIfNull(objektIds);
        ObjektIds = objektIds.Distinct().ToList();
    }

    public void Pruefen() => ArgumentException.ThrowIfNullOrWhiteSpace(Name);
}

public enum Umsatzstatus
{
    Offen = 0,
    Zugeordnet = 1,
    Ignoriert = 2,
}

/// <summary>Ein Kontoumsatz aus dem Bankimport; Betrag positiv = Eingang.</summary>
public sealed class Umsatz : Entitaet
{
    private Umsatz()
    {
        Hash = "";
        Verwendungszweck = "";
    }

    public Umsatz(Guid bankkontoId, Guid? bankimportId, GelesenerUmsatz gelesen, string hash)
    {
        ArgumentNullException.ThrowIfNull(gelesen);
        BankkontoId = bankkontoId;
        BankimportId = bankimportId;
        Buchungsdatum = gelesen.Buchungsdatum;
        Valutadatum = gelesen.Valutadatum;
        Betrag = gelesen.Betrag;
        Waehrung = gelesen.Waehrung;
        GegenName = gelesen.GegenName;
        GegenIban = gelesen.GegenIban;
        GegenBic = gelesen.GegenBic;
        Verwendungszweck = gelesen.Verwendungszweck;
        Buchungstext = gelesen.Buchungstext;
        EndToEndId = gelesen.EndToEndId;
        Mandatsreferenz = gelesen.Mandatsreferenz;
        GlaeubigerId = gelesen.GlaeubigerId;
        Hash = hash;
    }

    public Guid BankkontoId { get; private set; }

    public Guid? BankimportId { get; private set; }

    public DateOnly Buchungsdatum { get; private set; }

    public DateOnly? Valutadatum { get; private set; }

    public decimal Betrag { get; private set; }

    public string Waehrung { get; private set; } = "EUR";

    public string? GegenName { get; private set; }

    [Vertraulich]
    public string? GegenIban { get; private set; }

    public string? GegenBic { get; private set; }

    public string Verwendungszweck { get; private set; }

    /// <summary>Art der Buchung laut Bank, z. B. „Gutschrift“, „Lastschrift“.</summary>
    public string? Buchungstext { get; private set; }

    /// <summary>EREF.</summary>
    public string? EndToEndId { get; private set; }

    /// <summary>MREF.</summary>
    public string? Mandatsreferenz { get; private set; }

    /// <summary>CRED.</summary>
    public string? GlaeubigerId { get; private set; }

    public Umsatzstatus Status { get; private set; }

    public string? Notiz { get; set; }

    /// <summary>Fingerabdruck zur Duplikaterkennung, eindeutig je Konto.</summary>
    public string Hash { get; private set; }

    public void Ignorieren(string? notiz)
    {
        if (Status == Umsatzstatus.Zugeordnet)
        {
            throw new RegelverletzungException("Ein zugeordneter Umsatz kann nicht ignoriert werden; erst die Zuordnung lösen.");
        }

        Status = Umsatzstatus.Ignoriert;
        Notiz = notiz;
    }

    public void Oeffnen()
    {
        if (Status == Umsatzstatus.Zugeordnet)
        {
            throw new RegelverletzungException("Erst die Zuordnung lösen.");
        }

        Status = Umsatzstatus.Offen;
    }

    public void AlsZugeordnetMarkieren() => Status = Umsatzstatus.Zugeordnet;
}

/// <summary>Ein aus einer Datei gelesener, noch nicht gespeicherter Umsatz.</summary>
public sealed record GelesenerUmsatz(
    DateOnly Buchungsdatum,
    decimal Betrag,
    string Verwendungszweck,
    DateOnly? Valutadatum = null,
    string Waehrung = "EUR",
    string? GegenName = null,
    string? GegenIban = null,
    string? GegenBic = null,
    string? Buchungstext = null,
    string? EndToEndId = null,
    string? Mandatsreferenz = null,
    string? GlaeubigerId = null);

public enum Bankformat
{
    Camt053 = 0,
    Mt940 = 1,
    Csv = 2,
}

public enum Importstatus
{
    Vorschau = 0,
    Importiert = 1,
}

/// <summary>Hochgeladene Kontoauszugsdatei: erst Vorschau, nach Bestätigung importiert (Importprotokoll).</summary>
public sealed class Bankimport : Entitaet
{
    private Bankimport()
    {
        Dateiname = "";
        Inhalt = [];
    }

    public Bankimport(Guid bankkontoId, string dateiname, byte[] inhalt, Bankformat format, string? profil)
    {
        BankkontoId = bankkontoId;
        Dateiname = dateiname;
        Inhalt = inhalt;
        Format = format;
        Profil = profil;
    }

    public Guid BankkontoId { get; private set; }

    public string Dateiname { get; private set; }

    public Bankformat Format { get; private set; }

    /// <summary>Name des CSV-Profils.</summary>
    public string? Profil { get; private set; }

    /// <summary>Nur bis zum Import gespeichert.</summary>
    [OhneExport]
    public byte[] Inhalt { get; private set; }

    public Importstatus Status { get; private set; }

    public int Gelesen { get; private set; }

    public int Neu { get; private set; }

    public int Duplikate { get; private set; }

    public DateOnly? Von { get; private set; }

    public DateOnly? Bis { get; private set; }

    public void Abschliessen(int gelesen, int neu, int duplikate, DateOnly? von, DateOnly? bis)
    {
        if (Status == Importstatus.Importiert)
        {
            throw new RegelverletzungException("Diese Datei wurde bereits importiert.");
        }

        Status = Importstatus.Importiert;
        Gelesen = gelesen;
        Neu = neu;
        Duplikate = duplikate;
        Von = von;
        Bis = bis;
        Inhalt = [];
    }
}
