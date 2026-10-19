namespace MietHahn.Domain.Common;

/// <summary>
/// A business rule was violated (e.g. retroactive change, overlap). <see cref="Exception.Message"/>
/// is phrased for users and shown in the UI.
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
