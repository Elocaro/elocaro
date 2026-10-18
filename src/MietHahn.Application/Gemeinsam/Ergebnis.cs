namespace MietHahn.Application.Gemeinsam;

/// <summary>Ausgang eines Befehls aus der Oberfläche. <see cref="Fehler"/> sind für Benutzer formuliert.</summary>
public sealed record Ergebnis(bool Erfolgreich, IReadOnlyList<string> Fehler, Guid? Id = null)
{
    public static Ergebnis Ok(Guid? id = null) => new(true, [], id);

    public static Ergebnis Fehlgeschlagen(params string[] fehler) => new(false, fehler);
}
