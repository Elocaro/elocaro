namespace MietHahn.Application.Gemeinsam;

/// <summary>Wer gerade handelt, z. B. für Audit-Felder. Hintergrundjobs liefern <see cref="System"/>.</summary>
public interface IAktuellerBenutzer
{
    /// <summary>Kennung für Audit-Zwecke (Benutzername oder <see cref="System"/>).</summary>
    string Kennung { get; }

    public const string System = "system";
}
