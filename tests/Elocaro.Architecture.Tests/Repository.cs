namespace Elocaro.Architecture.Tests;

/// <summary>Locates the source tree for rules that read files instead of assemblies.</summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string Web { get; } = Path.Combine(Root, "src", "Elocaro.Web");

    public static string Components { get; } = Path.Combine(Web, "Components");

    public static string Infrastructure { get; } = Path.Combine(Root, "src", "Elocaro.Infrastructure");

    /// <summary>Path relative to <paramref name="root"/> with forward slashes.</summary>
    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Elocaro.Web")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
