using System.Text.RegularExpressions;

namespace MietHahn.Architecture.Tests;

/// <summary>
/// Module boundaries inside the Infrastructure project (a folder per functional area). They are folders, not assemblies, so nothing
/// else keeps them apart: two modules that use each other's types form a cycle. The existing cycles are frozen here; a new one fails the
/// test, a resolved one has to be taken out of the list, so the list only shrinks.
/// </summary>
public partial class ModuleRulesTests
{
    /// <summary>Pairs of modules that use each other today (alphabetical), counting namespaces of Application, Domain and Infrastructure. Goal: empty.</summary>
    private static readonly string[] KnownCycles =
    [
        "Leases <-> Properties",
    ];

    [Fact]
    public void Modules_do_not_form_new_cycles_and_resolved_ones_leave_the_list()
    {
        var uses = ModuleUses();
        var cycles = uses.Keys
            .SelectMany(a => uses[a].Where(b => uses.TryGetValue(b, out var back) && back.Contains(a)).Select(b => string.CompareOrdinal(a, b) < 0 ? $"{a} <-> {b}" : $"{b} <-> {a}"))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var added = cycles.Except(KnownCycles).ToList();
        var resolved = KnownCycles.Except(cycles).ToList();
        Assert.True(added.Count == 0, "New dependency cycle between modules (docs/architektur.md): move the shared part into Common or an interface of its own.\n" + string.Join("\n", added));
        Assert.True(resolved.Count == 0, "Cycle resolved, remove it from KnownCycles:\n" + string.Join("\n", resolved));
    }

    [Fact]
    public void Top_level_modules_are_used_by_no_other_module()
    {
        // Import, setup, overview figures, legal references and the handover protocol and the letters read from many modules for the screens; nothing may build on them.
        string[] topLevel = ["Setup", "Portfolio", "Legal", "Handover", "Correspondence"];
        var uses = ModuleUses();
        var violations = uses.Where(kv => !topLevel.Contains(kv.Key))
            .SelectMany(kv => kv.Value.Where(topLevel.Contains).Select(u => $"{kv.Key} -> {u}"))
            .ToList();

        Assert.True(violations.Count == 0, "A top-level module must not be used by other modules:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Foundations_do_not_depend_on_business_modules()
    {
        // Technical building blocks used by many; they must not reach back into business modules.
        string[] foundations = ["Common", "Admin", "Identity", "Files", "Mail", "Templates", "Notifications", "Audit"]; // Admin = module switches, Identity = accounts and roles
        var uses = ModuleUses();
        var violations = foundations.Where(uses.ContainsKey)
            .SelectMany(m => uses[m].Where(u => !foundations.Contains(u) && u != "Common").Select(u => $"{m} -> {u}"))
            .ToList();

        Assert.True(violations.Count == 0, "Foundations must not depend on business modules:\n" + string.Join("\n", violations));
    }

    /// <summary>Which other modules each module (folder) of Infrastructure uses, read from the namespaces it imports.</summary>
    private static Dictionary<string, HashSet<string>> ModuleUses()
    {
        var modules = Directory.GetDirectories(Repository.Infrastructure)
            .Select(d => Path.GetFileName(d))
            .Where(n => n is not ("bin" or "obj" or "Persistence"))
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Repository.Infrastructure, module), "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in UsingPattern().Matches(File.ReadAllText(file)))
                {
                    var other = match.Groups[1].Value;
                    if (other != module && modules.Contains(other))
                    {
                        used.Add(other);
                    }
                }
            }

            result[module] = used;
        }

        return result;
    }

    [GeneratedRegex(@"using (?:static )?MietHahn\.(?:Application|Domain|Infrastructure)\.([A-Za-z]+)")]
    private static partial Regex UsingPattern();
}
