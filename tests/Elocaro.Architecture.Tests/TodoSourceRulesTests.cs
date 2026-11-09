using System.Text.RegularExpressions;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// Every dashboard hint source has to be registered: a class that implements <c>ITodoSource</c> but is missing in
/// <c>DependencyInjection.cs</c> compiles and is documented, but its hints never show up (this happened with the reading reports).
/// </summary>
public partial class TodoSourceRulesTests
{
    [Fact]
    public void Every_todo_source_is_registered()
    {
        var registrations = File.ReadAllText(Path.Combine(Repository.Infrastructure, "DependencyInjection.cs"));
        var missing = Directory.EnumerateFiles(Repository.Infrastructure, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => TodoSourceClass().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(name => !Regex.IsMatch(registrations, $@"AddScoped<ITodoSource,\s*(\w+\.)*{name}>"))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "ITodoSource implementations without registration in DependencyInjection.cs:\n" + string.Join("\n", missing));
    }

    [GeneratedRegex(@"class\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*(?:[\w<>, ]+,\s*)?ITodoSource\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TodoSourceClass();
}
