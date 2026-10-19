namespace MietHahn.Application.Properties;

/// <summary>
/// Wer ein Mietobjekt (Einheit oder Zimmer) an einem Tag gemietet hat. Liefert das Vertragsmodul;
/// fehlt ein Eintrag, steht das Mietobjekt leer.
/// </summary>
public interface IOccupancyLookup
{
    Task<IReadOnlyDictionary<Guid, Occupancy>> AtAsync(IReadOnlyCollection<Guid> rentalUnitIds, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Gibt oder gab es Verträge für das Mietobjekt (dann darf es nicht gelöscht werden)?</summary>
    Task<bool> HasLeasesAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);
}

/// <param name="Tenant">Anzeigename(n) der Mieter, z. B. „Anna Schmidt, Ben Weber“.</param>
public sealed record Occupancy(Guid LeaseId, string Tenant, DateOnly Start, DateOnly? End);
