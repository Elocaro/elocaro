namespace MietHahn.Application.Common;

/// <summary>Wer gerade handelt, z. B. für Audit-Felder. Hintergrundjobs liefern <see cref="System"/>.</summary>
public interface ICurrentUser
{
    /// <summary>Kennung für Audit-Zwecke (Benutzername oder <see cref="System"/>).</summary>
    string Identifier { get; }

    public const string System = "system";
}
