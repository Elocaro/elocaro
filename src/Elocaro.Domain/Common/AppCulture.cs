using System.Globalization;

namespace Elocaro.Domain.Common;

/// <summary>
/// The application speaks German: amounts, numbers and dates in messages, mails and documents use de-DE, no matter what the
/// system language is. Container images and build servers have none (invariant culture), which turned "1.950,00 €" into "1950.00 €".
/// </summary>
public static class AppCulture
{
    /// <summary>The culture of all texts the application produces.</summary>
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Makes German the default of every thread that has no culture of its own (requests, background jobs, tests).
    /// Safe to call repeatedly.
    /// </summary>
    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }
}
