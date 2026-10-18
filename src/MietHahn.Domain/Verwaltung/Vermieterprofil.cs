using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Verwaltung;

/// <summary>
/// Absenderangaben des Vermieters für Briefkopf, Fußzeile und Zahlungsinformationen in Dokumenten.
/// Aktuell gibt es genau ein Profil; mit „Eigentümer je Objekt“ (M7) wird es je Objekt zugeordnet.
/// </summary>
public sealed class Vermieterprofil : Entitaet
{
    private Vermieterprofil()
    {
        Name = "";
        Strasse = "";
        Plz = "";
        Ort = "";
    }

    public Vermieterprofil(string name, string strasse, string plz, string ort)
    {
        Name = name;
        Strasse = strasse;
        Plz = plz;
        Ort = ort;
        Pruefen();
    }

    public string Name { get; set; }

    /// <summary>Optionaler Zusatz unter dem Namen, z. B. „Hausverwaltung Hauptstraße 5“.</summary>
    public string? Zusatz { get; set; }

    public string Strasse { get; set; }

    public string Plz { get; set; }

    public string Ort { get; set; }

    public string? Telefon { get; set; }

    public string? Email { get; set; }

    [Vertraulich]
    public string? Iban { get; set; }

    public string? Bic { get; set; }

    public string? Bank { get; set; }

    public string? Steuernummer { get; set; }

    /// <summary>Einzeilige Absenderangabe für die Rücksendezeile im Anschriftfeld.</summary>
    public string Ruecksendezeile => $"{Name} · {Strasse} · {Plz} {Ort}";

    public void Pruefen()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Strasse);
        ArgumentException.ThrowIfNullOrWhiteSpace(Plz);
        ArgumentException.ThrowIfNullOrWhiteSpace(Ort);
    }
}
