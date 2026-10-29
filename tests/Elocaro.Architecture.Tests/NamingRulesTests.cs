namespace Elocaro.Architecture.Tests;

/// <summary>
/// The product was renamed (see docs/umbenennung.md). The former name must not creep back into the source tree,
/// neither in file names nor in file contents.
/// </summary>
public class NamingRulesTests
{
    // Assembled so that this file does not match itself.
    private static readonly string[] FormerNames = ["Miet" + "Hahn", "Miet-" + "Hahn", "Miet " + "Hahn"];

    /// <summary>Files that talk about the former name on purpose (history, the rename itself, a real mailbox).</summary>
    private static readonly string[] Allowed =
    [
        "docs/namenspruefung.md",
        "docs/umbenennung.md",
        "CHANGELOG.md",
        "site/impressum.html",
        "site/datenschutz.html",
    ];

    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "node_modules", "TestResults", "data", ".vs", ".vscode", ".claude"];

    private static readonly string[] TextExtensions =
    [
        ".cs", ".razor", ".css", ".js", ".json", ".md", ".html", ".svg", ".yml", ".yaml", ".sh", ".csproj", ".props", ".targets",
        ".slnx", ".xml", ".txt", ".webmanifest", ".editorconfig", ".sql", ".scriban", ".example", ".dockerignore", ".gitattributes",
    ];

    [Fact]
    public void The_former_product_name_does_not_appear_in_file_names()
    {
        var offenders = Files().Where(f => Contains(Path.GetFileName(f)) || Contains(Relative(f).Split('/')[0])).Select(Relative).ToList();
        Assert.True(offenders.Count == 0, "File names with the former product name:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_former_product_name_does_not_appear_in_file_contents()
    {
        var offenders = new List<string>();
        foreach (var file in Files())
        {
            var relative = Relative(file);
            if (Allowed.Contains(relative, StringComparer.Ordinal) || !IsText(file))
            {
                continue;
            }

            if (Contains(File.ReadAllText(file)))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0, "Files with the former product name (rename them or, for history, add them to the list of allowed files):\n" + string.Join('\n', offenders));
    }

    private static bool Contains(string text) => FormerNames.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => Repository.Relative(Repository.Root, path);

    private static bool IsText(string file)
    {
        var name = Path.GetFileName(file);
        return TextExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || name is "Dockerfile" or "Caddyfile" or ".env.example";
    }

    private static IEnumerable<string> Files()
    {
        var pending = new Stack<string>([Repository.Root]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(sub), StringComparer.Ordinal))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }
        }
    }
}
