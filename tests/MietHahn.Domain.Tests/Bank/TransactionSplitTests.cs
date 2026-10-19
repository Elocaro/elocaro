using MietHahn.Domain.Bank;
using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Bank;

public class TransactionSplitTests
{
    [Fact]
    public void Split_into_two_cost_types_adds_up_exactly_to_the_amount()
    {
        var parts = TransactionSplit.Resolve(-1234.56m, [800.33m, 434.23m]);

        Assert.Equal([-800.33m, -434.23m], parts);
        Assert.Equal(-1234.56m, parts.Sum());
    }

    [Fact]
    public void Empty_part_receives_the_remainder()
    {
        Assert.Equal([100m, 61.45m], TransactionSplit.Resolve(161.45m, [100m, null]));
    }

    [Theory]
    [InlineData(100.01, 60, 40)]
    [InlineData(100, 60, 39.99)]
    public void Parts_that_do_not_add_up_are_rejected_with_the_difference(double total, double first, double second)
    {
        var error = Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve((decimal)total, [(decimal)first, (decimal)second]));

        Assert.Contains("Differenz 0,01 €", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_parts_are_rejected()
    {
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, []));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, [null, null]));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, [0m, 100m]));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, [-10m, 110m]));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, [33.333m, 66.667m]));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(100m, [100m, null]));
        Assert.Throws<RuleViolationException>(() => TransactionSplit.Resolve(0m, [null]));
    }
}
