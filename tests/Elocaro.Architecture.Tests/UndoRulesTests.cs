using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Audit;

namespace Elocaro.Architecture.Tests;

/// <summary>ADR 0007: every entity type is classified by how a change to it can be taken back. A new type fails here until somebody decides.</summary>
public class UndoRulesTests
{
    private static List<string> EntityTypes() =>
        typeof(Entity).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Entity))).Select(t => t.Name).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Every_entity_type_is_classified()
    {
        var missing = EntityTypes().Where(n => !UndoPolicy.All.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0, "Entity types without a class in UndoPolicy: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_classification_names_no_unknown_type()
    {
        var known = EntityTypes().ToHashSet();
        var unknown = UndoPolicy.All.Keys.Where(n => !known.Contains(n)).ToList();
        Assert.True(unknown.Count == 0, "UndoPolicy names types that do not exist: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Types_with_secrets_or_outside_effects_are_not_restorable()
    {
        foreach (var name in new[] { "BankDetails", "BankAccount", "OutgoingMail", "SignatureRequest", "StoredFile" })
        {
            Assert.False(UndoPolicy.IsRestorable(name), name);
        }
    }
}
