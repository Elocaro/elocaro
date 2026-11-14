using System.Text.RegularExpressions;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// Extension points are Application interfaces with several implementations that a consumer collects as a list (dashboard hints,
/// deadlines, personal data, references to a person, file shares, usage of an option). Each implementation has to be registered: a
/// class that implements the interface but is missing in <c>DependencyInjection.cs</c> compiles and is documented, but is never asked
/// (this happened with the reading reports on the dashboard).
/// </summary>
public partial class ExtensionPointRulesTests
{
    /// <summary>The extension points known today; a new one is found by <see cref="Every_extension_point_is_known"/>.</summary>
    private static readonly string[] Known = ["ITodoSource", "IDeadlineSource", "IPersonalDataSource", "IPersonReference", "IFileShare", "IOptionUsageSource"];

    public static TheoryData<string> ExtensionPoints => [.. Known];

    [Theory]
    [MemberData(nameof(ExtensionPoints))]
    public void Every_implementation_of_an_extension_point_is_registered(string extensionPoint)
    {
        var registrations = File.ReadAllText(Path.Combine(Repository.Infrastructure, "DependencyInjection.cs"));
        var implementation = new Regex($@"class\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*(?:[\w<>, ]+,\s*)?{extensionPoint}\b", RegexOptions.None, TimeSpan.FromSeconds(1));
        var implementations = SourceFiles(Repository.Infrastructure)
            .SelectMany(f => implementation.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var missing = implementations.Where(name => !Regex.IsMatch(registrations, $@"AddScoped<{extensionPoint},\s*(\w+\.)*{name}>", RegexOptions.None, TimeSpan.FromSeconds(1))).ToList();

        Assert.True(implementations.Count > 1, $"{extensionPoint} has no implementations; the rule would pass without checking anything.");
        Assert.True(missing.Count == 0, $"{extensionPoint} implementations without registration in DependencyInjection.cs:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_extension_point_is_known()
    {
        var contracts = SourceFiles(Repository.Application).SelectMany(f => InterfaceDeclaration().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        var collected = new[] { Repository.Infrastructure, Repository.Web }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)) && !Repository.IsBuildOutput(f))
            .SelectMany(f => CollectedAsList().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(contracts.Contains)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var unknown = collected.Except(Known).ToList();
        Assert.True(unknown.Count == 0, "An interface collected as a list is an extension point, add it to Known:\n" + string.Join("\n", unknown));
    }

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories).Where(f => !Repository.IsBuildOutput(f));

    [GeneratedRegex(@"public interface (I\w+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InterfaceDeclaration();

    [GeneratedRegex(@"(?:IEnumerable|IReadOnlyList|IReadOnlyCollection|GetServices)<(I[A-Z]\w+)>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CollectedAsList();
}
