using System.Text.RegularExpressions;
using Elocaro.Domain.Common;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// The audit log stores the CLR name of the entity type. A page that asks for the history under another name (a former or misspelled
/// one) compiles and simply shows nothing, so the names used in the pages are checked against the entity types.
/// </summary>
public partial class AuditRulesTests
{
    /// <summary>Types with a history that are no domain entities (accounts are written through <c>AuditWriter</c>).</summary>
    private static readonly string[] OtherAuditedTypes = ["ApplicationUser"];

    [Fact]
    public void Change_history_asks_for_an_existing_entity_type()
    {
        var known = typeof(Entity).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Entity))).Select(t => t.Name).Concat(OtherAuditedTypes).ToHashSet(StringComparer.Ordinal);
        var uses = Directory.EnumerateFiles(Repository.Components, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => HistoryType().Matches(File.ReadAllText(f)).Select(m => (File: Repository.Relative(Repository.Components, f), Type: m.Groups[1].Value)))
            .ToList();

        var unknown = uses.Where(u => !known.Contains(u.Type)).Select(u => $"{u.File}: {u.Type}").ToList();
        Assert.NotEmpty(uses);
        Assert.True(unknown.Count == 0, "ChangeHistory with an entity type that does not exist (use @nameof(…)):\n" + string.Join("\n", unknown));
    }

    /// <summary>The type name of a ChangeHistory, written as text or as the last part of a nameof.</summary>
    [GeneratedRegex(@"<ChangeHistory\s+EntityType=""(?:@nameof\((?:\w+\.)*)?(\w+)\)?""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HistoryType();
}
