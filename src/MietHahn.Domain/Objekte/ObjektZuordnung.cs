using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Objekte;

/// <summary>Ein Benutzerkonto (Hausmeister) betreut ein Objekt und sieht es im Servicebereich.</summary>
public sealed class ObjektZuordnung : Entitaet
{
    private ObjektZuordnung()
    {
    }

    public ObjektZuordnung(Guid objektId, Guid benutzerId)
    {
        ObjektId = objektId;
        BenutzerId = benutzerId;
    }

    public Guid ObjektId { get; private set; }

    public Guid BenutzerId { get; private set; }
}
