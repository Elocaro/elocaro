using System.Text.RegularExpressions;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// Module boundaries (a folder per functional area with the same name in Domain, Application and Infrastructure). They are folders, not
/// assemblies, so nothing else keeps them apart: two modules that use each other's types form a cycle. The existing cycles are frozen
/// here per layer; a new one fails the test, a resolved one has to be taken out of the list, so the lists only shrink.
/// </summary>
public partial class ModuleRulesTests
{
    /// <summary>Pairs of Infrastructure modules that use each other today (alphabetical), counting namespaces of Application, Domain and Infrastructure. Goal: empty.</summary>
    private static readonly string[] KnownCycles =
    [
        "Bank <-> Tax",
        "Leases <-> People",
        "Leases <-> Properties",
        "People <-> Properties",
        "Properties <-> Receipts",
    ];

    /// <summary>Pairs of Domain modules that use each other today. Goal: empty.</summary>
    private static readonly string[] KnownDomainCycles = [];

    /// <summary>Places where a foundation uses a business module today. Goal: empty.</summary>
    private static readonly string[] KnownFoundationUses =
    [
        "Application: Admin -> Leases", // ModuleCatalog names the rent models of the options
    ];

    /// <summary>The rules hold in each layer on its own; a module is the folder of the same name in each of them.</summary>
    private static readonly (string Name, string Project)[] Layers =
    [
        ("Domain", Repository.Domain),
        ("Application", Repository.Application),
        ("Infrastructure", Repository.Infrastructure),
    ];

    [Fact]
    public void Modules_do_not_form_new_cycles_and_resolved_ones_leave_the_list() => AssertCycles(Repository.Infrastructure, KnownCycles);

    [Fact]
    public void Domain_modules_do_not_form_new_cycles_and_resolved_ones_leave_the_list() => AssertCycles(Repository.Domain, KnownDomainCycles);

    [Fact]
    public void Application_modules_form_no_cycles() => AssertCycles(Repository.Application, []);

    private static void AssertCycles(string project, string[] known)
    {
        var uses = ModuleUses(project);
        var cycles = uses.Keys
            .SelectMany(a => uses[a].Where(b => uses.TryGetValue(b, out var back) && back.Contains(a)).Select(b => string.CompareOrdinal(a, b) < 0 ? $"{a} <-> {b}" : $"{b} <-> {a}"))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var added = cycles.Except(known).ToList();
        var resolved = known.Except(cycles).ToList();
        Assert.True(added.Count == 0, "New dependency cycle between modules (docs/architektur.md): move the shared part into Common or an interface of its own.\n" + string.Join("\n", added));
        Assert.True(resolved.Count == 0, "Cycle resolved, remove it from the list of known cycles:\n" + string.Join("\n", resolved));
    }

    [Fact]
    public void Top_level_modules_are_used_by_no_other_module()
    {
        // Import, setup, overview figures, legal references, the handover protocol, the letters and the emergency folder read from many modules for the screens; nothing may build on them.
        string[] topLevel = ["Setup", "Portfolio", "Legal", "Handover", "Correspondence", "Emergency"];
        var violations = Layers
            .SelectMany(layer => ModuleUses(layer.Project)
                .Where(kv => !topLevel.Contains(kv.Key))
                .SelectMany(kv => kv.Value.Where(topLevel.Contains).Select(u => $"{layer.Name}: {kv.Key} -> {u}")))
            .ToList();

        Assert.True(violations.Count == 0, "A top-level module must not be used by other modules:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Foundations_do_not_depend_on_business_modules()
    {
        // Technical building blocks used by many; they must not reach back into business modules.
        // Admin = module switches, Identity = accounts and roles (Accounts in Application), Mail = Communication in Application.
        string[] foundations = ["Common", "Admin", "Identity", "Accounts", "Files", "Mail", "Communication", "Templates", "Notifications", "Audit"];
        var uses = Layers
            .SelectMany(layer => ModuleUses(layer.Project)
                .Where(kv => foundations.Contains(kv.Key))
                .SelectMany(kv => kv.Value.Where(u => !foundations.Contains(u)).Select(u => $"{layer.Name}: {kv.Key} -> {u}")))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var added = uses.Except(KnownFoundationUses).ToList();
        var resolved = KnownFoundationUses.Except(uses).ToList();
        Assert.True(added.Count == 0, "Foundations must not depend on business modules:\n" + string.Join("\n", added));
        Assert.True(resolved.Count == 0, "Resolved, remove it from KnownFoundationUses:\n" + string.Join("\n", resolved));
    }

    /// <summary>
    /// Which other modules each module (folder) of a project uses. Every mention of a module namespace in code counts: <c>using</c>
    /// (also <c>static</c>, <c>global</c> and aliases) as well as names written out in full or from the layer on
    /// (<c>Domain.Leases.Lease</c>), because a reference without a <c>using</c> is a dependency all the same. Comment lines do not count.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ModuleUses(string project)
    {
        var modules = Directory.GetDirectories(project)
            .Select(d => Path.GetFileName(d))
            .Where(n => n is not ("bin" or "obj" or "Persistence"))
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(project, module), "*.cs", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
                {
                    foreach (Match match in ReferencePattern().Matches(line))
                    {
                        var other = match.Groups[1].Value;
                        if (other != module && modules.Contains(other))
                        {
                            used.Add(other);
                        }
                    }
                }
            }

            result[module] = used;
        }

        return result;
    }

    [GeneratedRegex(@"(?<![\w.])(?:Elocaro\.)?(?:Application|Domain|Infrastructure)\.([A-Z][A-Za-z]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferencePattern();
}
