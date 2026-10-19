namespace MietHahn.Domain.Common;

/// <summary>
/// Eine fachliche Regel wurde verletzt (z. B. rückwirkende Änderung, Überschneidung). <see cref="Exception.Message"/>
/// ist für Benutzer formuliert und wird in der Oberfläche angezeigt.
/// </summary>
public sealed class RuleViolationException : Exception
{
    public RuleViolationException()
    {
    }

    public RuleViolationException(string message)
        : base(message)
    {
    }

    public RuleViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
