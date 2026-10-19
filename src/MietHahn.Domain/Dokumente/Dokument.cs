using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Dokumente;

public enum Dokumentkategorie
{
    Vertrag = 0,
    Protokoll = 1,
    Abrechnung = 2,
    Korrespondenz = 3,
    Rechnung = 4,
    Versicherung = 5,
    Energieausweis = 6,
    Grundriss = 7,
    Foto = 8,
    Hausordnung = 9,
    Sonstiges = 99,
}

/// <summary>
/// Ein abgelegtes Dokument (Datei aus der Dateiablage) mit Bezug auf Objekt, Einheit, Vertrag und/oder Person.
/// Sichtbar ist es intern; Freigaben machen es zusätzlich für die betroffenen Mieter bzw. zugeordneten Hausmeister sichtbar.
/// </summary>
public sealed class Dokument : Entitaet
{
    public const int MaxTags = 20;

    private Dokument()
    {
        Titel = "";
    }

    public Dokument(Guid dateiId, string titel, Dokumentkategorie kategorie)
    {
        DateiId = dateiId;
        Titel = titel;
        Kategorie = kategorie;
        Pruefen();
    }

    public Guid DateiId { get; private set; }

    public string Titel { get; set; }

    public Dokumentkategorie Kategorie { get; set; }

    /// <summary>Datum des Dokuments (z. B. Rechnungsdatum), nicht des Uploads.</summary>
    public DateOnly? Datum { get; set; }

    /// <summary>Gültig bis (z. B. Energieausweis, Versicherung) für Erinnerungen.</summary>
    public DateOnly? AblaufAm { get; set; }

    public List<string> Tags { get; private set; } = [];

    public string? Notiz { get; set; }

    public Guid? ObjektId { get; private set; }

    public Guid? EinheitId { get; private set; }

    public Guid? MietvertragId { get; private set; }

    public Guid? PersonId { get; private set; }

    /// <summary>Im Mieterportal sichtbar für die Mieter des Bezugs (Vertrag, Einheit, Objekt oder Person).</summary>
    public bool FuerMieterFreigegeben { get; set; }

    /// <summary>Im Servicebereich sichtbar für Hausmeister, denen das Objekt zugeordnet ist.</summary>
    public bool FuerHausmeisterFreigegeben { get; set; }

    public void BezugSetzen(Guid? objektId, Guid? einheitId, Guid? mietvertragId, Guid? personId)
    {
        if (objektId is null && einheitId is null && mietvertragId is null && personId is null)
        {
            throw new RegelverletzungException("Bitte mindestens einen Bezug wählen (Objekt, Einheit, Vertrag oder Person).");
        }

        ObjektId = objektId;
        EinheitId = einheitId;
        MietvertragId = mietvertragId;
        PersonId = personId;
    }

    /// <summary>Kommagetrennt eingegebene Schlagworte: getrimmt, ohne Doppelte (Groß/Klein egal).</summary>
    public void TagsSetzen(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var bereinigt = tags.Select(t => t.Trim()).Where(t => t.Length > 0).DistinctBy(t => t.ToLowerInvariant()).ToList();
        if (bereinigt.Count > MaxTags || bereinigt.Any(t => t.Length > 50))
        {
            throw new RegelverletzungException($"Höchstens {MaxTags} Schlagworte mit je bis zu 50 Zeichen.");
        }

        Tags = bereinigt;
    }

    public void Pruefen() => ArgumentException.ThrowIfNullOrWhiteSpace(Titel);
}
