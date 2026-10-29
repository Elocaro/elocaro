using Elocaro.Domain.Common;
using Elocaro.Domain.Deposits;

namespace Elocaro.Domain.Tests.Deposits;

public class DepositTests
{
    private static readonly Guid Lease = Guid.NewGuid();

    private static DepositEntry Entry(DepositEntryKind kind, decimal amount, string? note = null) =>
        new(Lease, kind, new DateOnly(2026, 1, 1), amount, note);

    [Fact]
    public void Balance_is_received_plus_interest_minus_refund_and_deduction()
    {
        var account = DepositAccount.Of(1950m, [
            Entry(DepositEntryKind.Received, 650m), Entry(DepositEntryKind.Received, 650m), Entry(DepositEntryKind.Received, 650m),
            Entry(DepositEntryKind.Interest, 12.5m), Entry(DepositEntryKind.Deduction, 100m, "Streichen"), Entry(DepositEntryKind.Refund, 400m)]);

        Assert.Equal(1950m, account.Received);
        Assert.Equal(12.5m, account.Interest);
        Assert.Equal(1962.5m - 500m, account.Balance);
        Assert.Equal(0m, account.Outstanding);
    }

    [Fact]
    public void Outstanding_is_the_agreed_deposit_minus_what_came_in_and_never_negative()
    {
        Assert.Equal(1300m, DepositAccount.Of(1950m, [Entry(DepositEntryKind.Received, 650m)]).Outstanding);
        Assert.Equal(0m, DepositAccount.Of(1000m, [Entry(DepositEntryKind.Received, 1200m)]).Outstanding);
        Assert.Equal(0m, DepositAccount.Of(null, []).Outstanding);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    public void Amount_must_be_positive_with_two_decimals(double amount)
    {
        Assert.Throws<RuleViolationException>(() => Entry(DepositEntryKind.Received, (decimal)amount));
    }

    [Fact]
    public void A_deduction_needs_to_say_what_it_is_set_off_against()
    {
        Assert.Throws<RuleViolationException>(() => Entry(DepositEntryKind.Deduction, 50m));
        Assert.Throws<RuleViolationException>(() => Entry(DepositEntryKind.Deduction, 50m, "  "));
        Assert.Equal("Nachzahlung 2025", Entry(DepositEntryKind.Deduction, 50m, " Nachzahlung 2025 ").Note);
    }

    [Fact]
    public void Effect_adds_for_received_and_interest_and_reduces_for_refund_and_deduction()
    {
        Assert.Equal(10m, Entry(DepositEntryKind.Received, 10m).Effect);
        Assert.Equal(10m, Entry(DepositEntryKind.Interest, 10m).Effect);
        Assert.Equal(-10m, Entry(DepositEntryKind.Refund, 10m).Effect);
        Assert.Equal(-10m, Entry(DepositEntryKind.Deduction, 10m, "x").Effect);
    }
}
