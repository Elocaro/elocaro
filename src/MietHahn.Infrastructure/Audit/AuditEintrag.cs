using MietHahn.Application.Audit;

namespace MietHahn.Infrastructure.Audit;

/// <summary>
/// Ein Eintrag im Audit-Log. Bewusst keine <see cref="Domain.Gemeinsam.Entitaet"/>: unveränderlich,
/// nicht logisch löschbar, wird nur durch die Aufbewahrungsregel entfernt.
/// </summary>
public sealed class AuditEintrag
{
    private AuditEintrag()
    {
        Benutzer = "";
        EntitaetTyp = "";
        AenderungenJson = "[]";
    }

    public AuditEintrag(DateTimeOffset zeitpunkt, string benutzer, string entitaetTyp, Guid entitaetId, AuditAktion aktion, string aenderungenJson, string? zweck = null)
    {
        Zeitpunkt = zeitpunkt;
        Benutzer = benutzer;
        EntitaetTyp = entitaetTyp;
        EntitaetId = entitaetId;
        Aktion = aktion;
        AenderungenJson = aenderungenJson;
        Zweck = zweck;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public DateTimeOffset Zeitpunkt { get; private set; }

    public string Benutzer { get; private set; }

    public string EntitaetTyp { get; private set; }

    public Guid EntitaetId { get; private set; }

    public AuditAktion Aktion { get; private set; }

    /// <summary>Liste von <see cref="Feldaenderung"/> als JSON (jsonb).</summary>
    public string AenderungenJson { get; private set; }

    /// <summary>Bei <see cref="AuditAktion.Gelesen"/>: Anlass des Zugriffs.</summary>
    public string? Zweck { get; private set; }
}
