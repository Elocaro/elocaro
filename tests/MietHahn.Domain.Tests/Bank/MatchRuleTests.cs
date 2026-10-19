using MietHahn.Domain.Bank;
using MietHahn.Domain.Bank.Matching;
using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Bank;

public class MatchRuleTests
{
    private static MatchInput Expense(string? name = "Stadtwerke Köln GmbH", string? iban = "DE89 3704 0044 0532 0130 00", string purpose = "Abschlag Strom 04/2026") =>
        new(new DateOnly(2026, 4, 3), -85m, name, iban, purpose);

    [Fact]
    public void All_conditions_must_apply_and_alternatives_are_separated_by_semicolon()
    {
        var rule = new MatchRule("Strom", RuleAction.Expense) { CostType = "betrkv-11", CounterpartyContains = "Stadtwerke; SWK", PurposeContains = "Strom" };

        Assert.True(rule.Matches(Expense()));
        Assert.True(rule.Matches(Expense(name: "SWK Energie")));
        Assert.False(rule.Matches(Expense(purpose: "Abschlag Gas")));
        Assert.False(rule.Matches(Expense(name: "Wasserwerk")));
    }

    [Fact]
    public void Counter_IBAN_is_compared_without_spaces_and_case()
    {
        var rule = new MatchRule("Wasser", RuleAction.Expense) { CostType = "betrkv-02" };
        rule.SetCounterpartyIban("de89370400440532013000");

        Assert.True(rule.Matches(Expense()));
        Assert.False(rule.Matches(Expense(iban: "DE02120300000000202051")));
        Assert.False(rule.Matches(Expense(iban: null)));
        Assert.Throws<RuleViolationException>(() => rule.SetCounterpartyIban("DE89370400440532013001"));
    }

    [Fact]
    public void Direction_restricts_the_rule_to_income_or_expenses()
    {
        var rule = new MatchRule("Nur Eingang", RuleAction.Ignore) { PurposeContains = "Strom", Direction = RuleDirection.Income };

        Assert.False(rule.Matches(Expense()));
        Assert.True(rule.Matches(Expense() with { Amount = 85m }));
    }

    [Fact]
    public void Validation_requires_condition_and_matching_target()
    {
        Assert.Throws<RuleViolationException>(() => new MatchRule("Leer", RuleAction.Ignore).Validate());
        Assert.Throws<RuleViolationException>(() => new MatchRule("", RuleAction.Ignore) { PurposeContains = "x" }.Validate());
        Assert.Throws<RuleViolationException>(() => new MatchRule("Ohne Kostenart", RuleAction.Expense) { PurposeContains = "x" }.Validate());
        Assert.Throws<RuleViolationException>(() => new MatchRule("Unbekannt", RuleAction.Expense) { PurposeContains = "x", CostType = "gibt-es-nicht" }.Validate());
        Assert.Throws<RuleViolationException>(() => new MatchRule("Ohne Vertrag", RuleAction.Lease) { PurposeContains = "x" }.Validate());
        Assert.Throws<RuleViolationException>(() => new MatchRule("Falsch", RuleAction.Expense) { PurposeContains = "x", CostType = "betrkv-02", Direction = RuleDirection.Income }.Validate());

        var valid = new MatchRule("Privat", RuleAction.Ignore) { PurposeContains = "privat", CostType = "betrkv-02", LeaseId = Guid.NewGuid() };
        valid.Validate();
        Assert.Null(valid.CostType);
        Assert.Null(valid.LeaseId);
    }
}
