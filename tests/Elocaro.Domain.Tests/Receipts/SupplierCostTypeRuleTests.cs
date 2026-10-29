using Elocaro.Domain.Common;
using Elocaro.Domain.Receipts;

namespace Elocaro.Domain.Tests.Receipts;

public class SupplierCostTypeRuleTests
{
    [Theory]
    [InlineData("Müller & Söhne GmbH", "müller söhne gmbh")]
    [InlineData("  Stadtwerke   Köln  ", "stadtwerke köln")]
    [InlineData("GROSSE-STRASSE Bau", "grosse strasse bau")]
    [InlineData("Weiß Heizöl", "weiss heizöl")]
    public void Names_are_normalized(string name, string expected) => Assert.Equal(expected, SupplierCostTypeRule.Normalize(name));

    [Fact]
    public void Key_prefers_the_contact_and_falls_back_to_the_name()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal("contact:" + id.ToString("N"), SupplierCostTypeRule.KeyFor(id, "egal"));
        Assert.Equal("name:stadtwerke köln", SupplierCostTypeRule.KeyFor(null, "Stadtwerke Köln"));
        Assert.Null(SupplierCostTypeRule.KeyFor(null, "  "));
    }

    [Fact]
    public void Learning_counts_confirmations_and_resets_on_a_different_cost_type()
    {
        var rule = new SupplierCostTypeRule("name:x", "betrkv-14");
        rule.Learn("betrkv-14");
        Assert.Equal(2, rule.Uses);

        rule.Learn("instandhaltung");
        Assert.Equal(1, rule.Uses);
        Assert.Equal("instandhaltung", rule.CostType);
    }

    [Fact]
    public void Unknown_cost_type_and_empty_key_are_rejected()
    {
        Assert.Throws<RuleViolationException>(() => new SupplierCostTypeRule("name:x", "nix"));
        Assert.Throws<RuleViolationException>(() => new SupplierCostTypeRule(" ", "betrkv-14"));
    }
}
