namespace MietHahn.Domain.Common;

/// <summary>
/// Basis aller persistierten Entitäten. Audit-Felder, Löschkennzeichen und Version setzt ausschließlich die
/// Infrastruktur beim Speichern (über den EF-Change-Tracker), daher sind die Setter privat.
/// Gelöscht wird nur logisch (<see cref="Deleted"/>).
/// </summary>
public abstract class Entity
{
    /// <summary>Zeitlich sortierbare GUID (v7), damit Indizes nicht fragmentieren.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public string? ModifiedBy { get; private set; }

    public bool Deleted { get; private set; }

    /// <summary>Optimistische Nebenläufigkeit (in PostgreSQL auf die Systemspalte <c>xmin</c> abgebildet).</summary>
    public uint Version { get; private set; }
}
