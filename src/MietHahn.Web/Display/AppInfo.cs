using System.Reflection;

namespace MietHahn.Web.Display;

/// <summary>Version and project links shown on the "Über MietHahn" page.</summary>
public static class AppInfo
{
    public const string SourceUrl = "https://github.com/TheR00st3r/MietHahn";
    public const string LicenseName = "GNU Affero General Public License v3.0";

    /// <summary>The release version (from the tag at build time); "Entwicklung" for local builds.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "Entwicklung";
        }

        var version = informational.Split('+')[0];
        return version is "1.0.0" or "0.0.0-dev" ? "Entwicklung" : version;
    }
}
