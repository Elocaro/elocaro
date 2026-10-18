namespace MietHahn.Domain.Gemeinsam;

/// <summary>
/// Eine fachliche Regel wurde verletzt (z. B. rückwirkende Änderung, Überschneidung). <see cref="Exception.Message"/>
/// ist für Benutzer formuliert und wird in der Oberfläche angezeigt.
/// </summary>
public sealed class RegelverletzungException : Exception
{
    public RegelverletzungException()
    {
    }

    public RegelverletzungException(string message)
        : base(message)
    {
    }

    public RegelverletzungException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
