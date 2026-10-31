namespace Elocaro.Application.Accounts;

/// <summary>The code that authorizes creating the first administrator of an empty installation.</summary>
public interface ISetupCode
{
    string Value { get; }

    /// <summary>Whether the code was generated at startup (not configured), so that nobody but the log knows it.</summary>
    bool IsGenerated { get; }

    /// <summary>Compares in constant time; surrounding whitespace from copying is ignored.</summary>
    bool Matches(string? candidate);
}
