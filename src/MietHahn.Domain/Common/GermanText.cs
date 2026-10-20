using System.Globalization;

namespace MietHahn.Domain.Common;

/// <summary>
/// German notation for user-visible texts that the domain and services compose. Never depends on the culture of the host
/// (a container without locale would otherwise print "238.00 €" in a German message).
/// </summary>
public static class GermanText
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>An amount like "1.234,50 €".</summary>
    public static string Money(decimal amount) => amount.ToString("#,##0.00", German) + " €";
}
