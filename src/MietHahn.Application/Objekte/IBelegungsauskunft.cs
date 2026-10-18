namespace MietHahn.Application.Objekte;

/// <summary>
/// Wer ein Mietobjekt (Einheit oder Zimmer) an einem Tag gemietet hat. Liefert das Vertragsmodul;
/// fehlt ein Eintrag, steht das Mietobjekt leer.
/// </summary>
public interface IBelegungsauskunft
{
    Task<IReadOnlyDictionary<Guid, Belegung>> AmAsync(IReadOnlyCollection<Guid> mietobjektIds, DateOnly tag, CancellationToken cancellationToken = default);

    /// <summary>Gibt oder gab es Verträge für das Mietobjekt (dann darf es nicht gelöscht werden)?</summary>
    Task<bool> HatVertraegeAsync(Guid mietobjektId, CancellationToken cancellationToken = default);
}

/// <param name="Mieter">Anzeigename(n) der Mieter, z. B. „Anna Schmidt, Ben Weber“.</param>
public sealed record Belegung(Guid VertragId, string Mieter, DateOnly Beginn, DateOnly? Ende);
