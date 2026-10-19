using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Dokumente;

namespace MietHahn.Application.Dokumente;

/// <summary>Dokumentablage je Objekt, Einheit, Vertrag und Person mit Freigaben für Mieter und Hausmeister.</summary>
public interface IDokumentablage
{
    Task<IReadOnlyList<DokumentZeile>> SucheAsync(DokumentSuche suche, CancellationToken cancellationToken = default);

    Task<DokumentDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Legt ein Dokument zu einer bereits gespeicherten Datei an.</summary>
    Task<Ergebnis> AnlegenAsync(Guid dateiId, DokumentDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, DokumentDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Für Mieter bzw. Hausmeister freigegebene Dokumente, die das Konto sehen darf (Portal, Servicebereich).</summary>
    Task<IReadOnlyList<DokumentZeile>> FreigegebeneAsync(ClaimsPrincipal benutzer, Guid? objektId = null, CancellationToken cancellationToken = default);

    /// <summary>Auswahllisten für den Bezug.</summary>
    Task<BezugAuswahl> BezugAuswahlAsync(CancellationToken cancellationToken = default);
}

/// <param name="Text">Sucht in Titel, Schlagworten und Dateiname.</param>
public sealed record DokumentSuche(
    string? Text = null,
    Dokumentkategorie? Kategorie = null,
    Guid? ObjektId = null,
    Guid? EinheitId = null,
    Guid? MietvertragId = null,
    Guid? PersonId = null);

public sealed class DokumentDaten
{
    [Required(ErrorMessage = "Bitte einen Titel angeben.")]
    [StringLength(200)]
    public string Titel { get; set; } = "";

    public Dokumentkategorie Kategorie { get; set; } = Dokumentkategorie.Sonstiges;

    public DateOnly? Datum { get; set; }

    public DateOnly? AblaufAm { get; set; }

    /// <summary>Kommagetrennt, z. B. „Heizung, Wartung 2026“.</summary>
    [StringLength(1000)]
    public string? Tags { get; set; }

    [StringLength(2000)]
    public string? Notiz { get; set; }

    public Guid? ObjektId { get; set; }

    public Guid? EinheitId { get; set; }

    public Guid? MietvertragId { get; set; }

    public Guid? PersonId { get; set; }

    public bool FuerMieterFreigegeben { get; set; }

    public bool FuerHausmeisterFreigegeben { get; set; }
}

public sealed record DokumentZeile(
    Guid Id,
    Guid DateiId,
    string Titel,
    Dokumentkategorie Kategorie,
    DateOnly? Datum,
    DateOnly? AblaufAm,
    IReadOnlyList<string> Tags,
    string Bezug,
    string MimeTyp,
    long Groesse,
    bool FuerMieter,
    bool FuerHausmeister);

public sealed record DokumentDetails(DokumentZeile Dokument, DokumentDaten Daten, string Dateiname, DateTimeOffset HochgeladenAm, string? HochgeladenVon);

public sealed record BezugOption(Guid Id, string Bezeichnung);

public sealed record BezugAuswahl(
    IReadOnlyList<BezugOption> Objekte,
    IReadOnlyList<BezugOption> Einheiten,
    IReadOnlyList<BezugOption> Vertraege,
    IReadOnlyList<BezugOption> Personen);
