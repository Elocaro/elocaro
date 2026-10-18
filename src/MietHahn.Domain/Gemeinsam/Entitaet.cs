namespace MietHahn.Domain.Gemeinsam;

/// <summary>
/// Basis aller persistierten Entitäten. Audit-Felder, Löschkennzeichen und Version setzt ausschließlich die
/// Infrastruktur beim Speichern (über den EF-Change-Tracker), daher sind die Setter privat.
/// Gelöscht wird nur logisch (<see cref="Geloescht"/>).
/// </summary>
public abstract class Entitaet
{
    /// <summary>Zeitlich sortierbare GUID (v7), damit Indizes nicht fragmentieren.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset ErstelltAm { get; private set; }

    public string? ErstelltVon { get; private set; }

    public DateTimeOffset? GeaendertAm { get; private set; }

    public string? GeaendertVon { get; private set; }

    public bool Geloescht { get; private set; }

    /// <summary>Optimistische Nebenläufigkeit (in PostgreSQL auf die Systemspalte <c>xmin</c> abgebildet).</summary>
    public uint Version { get; private set; }
}
