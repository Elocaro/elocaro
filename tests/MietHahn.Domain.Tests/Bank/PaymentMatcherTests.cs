using System.Globalization;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Bank.Matching;
using MietHahn.Domain.Finance;

namespace MietHahn.Domain.Tests.Bank;

public class PaymentMatcherTests
{
    private const string IbanA = "DE89370400440532013000";
    private const string IbanB = "DE02120300000000202051";

    private static LeaseCandidate Lease(Guid id, string unit, string first, string last, decimal rent, IEnumerable<OpenCharge>? open = null, string? iban = null, string? company = null) =>
        new(id, $"Haus, {unit}: {first} {last}", unit, new DateOnly(2025, 1, 1), null,
            [new TenantInfo(first, last, company, iban is null ? [] : [iban])], _ => rent, open?.ToList() ?? []);

    private static OpenCharge Rent(int year, int month, decimal amount, string name = "Kaltmiete") =>
        new(Guid.CreateVersion7(), ChargeType.Rent, new DateOnly(year, month, 3), new DateOnly(year, month, 1), amount, $"{name} {month:00}/{year}");

    private static MatchInput Income(decimal amount, string? name, string? iban, string purpose, int month = 3) =>
        new(new DateOnly(2026, month, 4), amount, name, iban, purpose);

    [Fact]
    public void Counter_IBAN_name_amount_and_purpose_lead_to_a_high_score_with_reasons()
    {
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 770m, [Rent(2026, 3, 620m), Rent(2026, 3, 150m, "Nebenkosten")], IbanA);
        var peter = Lease(Guid.NewGuid(), "Wohnung 3", "Peter", "Meier", 770m, [Rent(2026, 3, 620m), Rent(2026, 3, 150m, "Nebenkosten")], IbanB);

        var suggestions = PaymentMatcher.Suggest(Income(770m, "SCHMIDT, ANNA", IbanA, "Miete 03/26 Whg 2"), new MatchContext([], [peter, anna], []));

        var best = suggestions[0];
        Assert.Equal(anna.LeaseId, best.LeaseId);
        Assert.Equal(100, best.Score);
        Assert.Contains(best.Reasons, r => r.StartsWith("Gegen-IBAN", StringComparison.Ordinal));
        Assert.Contains(best.Reasons, r => r.StartsWith("Name passt", StringComparison.Ordinal));
        Assert.Contains(best.Reasons, r => r.StartsWith("Betrag entspricht", StringComparison.Ordinal));
        Assert.Contains(best.Reasons, r => r.Contains("Wohnung 2", StringComparison.Ordinal));
        Assert.Contains(best.Reasons, r => r.Contains("03/2026", StringComparison.Ordinal));
        Assert.Equal("hoch", best.Confidence);
        Assert.Equal([(null, 770m)], best.Parts.Select(p => (p.ChargeId, p.Amount)));
        Assert.True(suggestions.Count == 1 || suggestions[1].Score < best.Score);
    }

    [Fact]
    public void Unrelated_transactions_get_no_suggestion()
    {
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 770m, [Rent(2026, 3, 770m)], IbanA);

        Assert.Empty(PaymentMatcher.Suggest(Income(1234.56m, "Finanzamt Köln", null, "Erstattung Einkommensteuer"), new MatchContext([], [anna], [])));
    }

    [Fact]
    public void Collective_payment_of_three_months_is_split_into_the_open_charges()
    {
        var charges = new[] { Rent(2026, 1, 700m), Rent(2026, 2, 700m), Rent(2026, 3, 700m), Rent(2026, 4, 700m) };
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 700m, charges, IbanA);

        var best = PaymentMatcher.Suggest(Income(2100m, "Anna Schmidt", IbanA, "Miete Januar bis März 2026"), new MatchContext([], [anna], []))[0];

        Assert.Equal(charges.Take(3).Select(c => c.ChargeId), best.Parts.Select(p => p.ChargeId!.Value));
        Assert.Equal(2100m, best.Parts.Sum(p => p.Amount));
        Assert.Contains(best.Reasons, r => r.Contains("01/2026", StringComparison.Ordinal));
    }

    [Fact]
    public void Collective_payment_without_months_in_the_purpose_is_recognized_by_the_amount()
    {
        var charges = new[] { Rent(2026, 2, 500m), Rent(2026, 3, 500m) };
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 500m, charges, IbanA);

        var best = PaymentMatcher.Suggest(Income(1000m, "Anna Schmidt", IbanA, "Miete"), new MatchContext([], [anna], []))[0];

        Assert.Equal(2, best.Parts.Count);
        Assert.Contains(best.Reasons, r => r.Contains("Sammelzahlung", StringComparison.Ordinal));
    }

    [Fact]
    public void Partial_payment_is_one_free_part_for_the_allocation()
    {
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 770m, [Rent(2026, 3, 770m)], IbanA);

        var best = PaymentMatcher.Suggest(Income(400m, "Anna Schmidt", IbanA, "Miete Teilzahlung"), new MatchContext([], [anna], []))[0];

        Assert.Equal([(null, 400m)], best.Parts.Select(p => (p.ChargeId, p.Amount)));
    }

    [Fact]
    public void Rule_comes_before_the_scoring_and_overrides_the_ranking()
    {
        var anna = Lease(Guid.NewGuid(), "Wohnung 2", "Anna", "Schmidt", 770m, [Rent(2026, 3, 770m)], IbanA);
        var peter = Lease(Guid.NewGuid(), "Wohnung 3", "Peter", "Meier", 770m, [Rent(2026, 3, 770m)], IbanB);
        var rule = new MatchRule("Sozialamt zahlt für Meier", RuleAction.Lease) { CounterpartyContains = "Sozialamt", LeaseId = peter.LeaseId };

        var suggestions = PaymentMatcher.Suggest(Income(770m, "Sozialamt Köln", null, "Miete 03/2026 Frau Schmidt"), new MatchContext([rule], [anna, peter], []));

        Assert.Equal((peter.LeaseId, 100, rule.Id), (suggestions[0].LeaseId, suggestions[0].Score, suggestions[0].RuleId));
        Assert.Equal("Regel „Sozialamt zahlt für Meier“", suggestions[0].Reasons[0]);
        Assert.Equal(anna.LeaseId, suggestions[1].LeaseId);
    }

    [Fact]
    public void Rules_are_checked_by_priority_and_can_ignore_or_book_expenses()
    {
        var property = Guid.NewGuid();
        var ignore = new MatchRule("Privat", RuleAction.Ignore) { PurposeContains = "Amazon", Priority = 10 };
        var water = new MatchRule("Wasser", RuleAction.Expense) { CounterpartyContains = "Stadtwerke", CostType = "betrkv-02", PropertyId = property, Priority = 50 };
        var power = new MatchRule("Allgemeinstrom", RuleAction.Expense) { CounterpartyContains = "Stadtwerke", CostType = "betrkv-11", PropertyId = property, Priority = 60 };
        var context = new MatchContext([power, water, ignore], [], []);
        var expense = new MatchInput(new DateOnly(2026, 3, 5), -88m, "Stadtwerke Musterstadt", null, "Abschlag");

        var booked = Assert.Single(PaymentMatcher.Suggest(expense, context));
        Assert.Equal((SuggestionKind.Expense, "betrkv-02", property), (booked.Kind, booked.CostType, booked.PropertyId));

        var ignored = Assert.Single(PaymentMatcher.Suggest(expense with { Purpose = "Amazon Bestellung" }, context));
        Assert.Equal(SuggestionKind.Ignore, ignored.Kind);
    }

    [Fact]
    public void Expense_without_rule_uses_earlier_assignment_of_the_same_payee()
    {
        var property = Guid.NewGuid();
        var history = new[]
        {
            new ExpenseHistory(IbanB, "Kaminkehrer Huber", "betrkv-12", property, null, true),
            new ExpenseHistory(IbanA, "Stadtwerke", "betrkv-11", property, null, true),
        };

        var byIban = PaymentMatcher.Suggest(new MatchInput(new DateOnly(2026, 3, 5), -50m, "SW Musterstadt", "DE89 3704 0044 0532 0130 00", "Abschlag"), new MatchContext([], [], history));
        var byName = PaymentMatcher.Suggest(new MatchInput(new DateOnly(2026, 3, 5), -50m, "Kaminkehrer HUBER", null, "Rechnung"), new MatchContext([], [], history));

        Assert.Equal(("betrkv-11", 75), (byIban[0].CostType, byIban[0].Score));
        Assert.Equal(("betrkv-12", 60), (byName[0].CostType, byName[0].Score));
        Assert.Empty(PaymentMatcher.Suggest(new MatchInput(new DateOnly(2026, 3, 5), -50m, "Unbekannt", null, "Rechnung"), new MatchContext([], [], history)));
    }

    /// <summary>
    /// Acceptance criterion of the issue: a test set of one year of rent transactions is suggested correctly in at least 90 % of the cases.
    /// The set contains typical variations: name formats, typos, other payers, missing IBAN, different purposes, partial and collective payments.
    /// </summary>
    [Fact]
    public void One_year_of_generated_transactions_is_suggested_correctly_in_at_least_90_percent()
    {
        var (transactions, expected, leases) = Year2025();

        var correct = 0;
        var failures = new List<string>();
        var open = leases.ToDictionary(l => l.LeaseId, l => l.OpenCharges.ToList());
        for (var i = 0; i < transactions.Count; i++)
        {
            var tx = transactions[i];

            // Open charges at the booking date: charges that are due and not yet settled by earlier payments.
            var context = leases.Select(l => l with { OpenCharges = OpenAt(open[l.LeaseId], tx.BookingDate) }).ToList();
            var suggestions = PaymentMatcher.Suggest(tx, new MatchContext([], context, []));
            if (suggestions.Count > 0 && suggestions[0].LeaseId == expected[i])
            {
                correct++;
            }
            else
            {
                failures.Add($"{tx.BookingDate:yyyy-MM-dd} {tx.CounterpartyName} | {tx.Purpose} | {tx.Amount} -> {(suggestions.Count == 0 ? "-" : suggestions[0].Score.ToString(CultureInfo.InvariantCulture))}");
            }

            // Simulate the allocation: the payment settles the oldest charges of the lease.
            Settle(open[expected[i]], tx.Amount);
        }

        var rate = correct * 100.0 / transactions.Count;
        Assert.True(transactions.Count >= 90, $"Testmenge zu klein: {transactions.Count}");
        Assert.True(rate >= 90.0, $"Trefferquote {rate:0.0} % ({correct}/{transactions.Count}); falsch: {string.Join("; ", failures)}");
    }

    private static List<OpenCharge> OpenAt(List<OpenCharge> charges, DateOnly day) => charges.Where(c => c.Open > 0 && c.Due <= day).ToList();

    private static void Settle(List<OpenCharge> charges, decimal amount)
    {
        var rest = amount;
        for (var i = 0; i < charges.Count && rest > 0; i++)
        {
            var paid = Math.Min(rest, charges[i].Open);
            charges[i] = charges[i] with { Open = charges[i].Open - paid };
            rest -= paid;
        }
    }

    private sealed record Tenant(string First, string Last, string? Company, string Unit, decimal Base, decimal Utilities, string Iban, string? OtherPayer);

    private static (List<MatchInput> Transactions, List<Guid> Expected, List<LeaseCandidate> Leases) Year2025()
    {
        // Same last name and rent for two leases on purpose.
        var tenants = new[]
        {
            new Tenant("Hans", "Mustermann", null, "Wohnung 1", 620m, 150m, "DE89370400440532013000", "Erika Mustermann"),
            new Tenant("Erika", "Musterfrau", null, "Wohnung 2", 500m, 150m, "DE02120300000000202051", null),
            new Tenant("Peter", "Müller", null, "Wohnung 3", 560m, 160m, "DE02500105170137075030", null),
            new Tenant("Anna", "Müller", null, "Wohnung 4", 560m, 160m, "DE75512108001245126199", "Sozialamt"),
            new Tenant("", "Schmidt & Söhne GmbH", "Schmidt & Söhne GmbH", "Wohnung 5", 1000m, 250m, "DE91100000000123456789", null),
            new Tenant("Mehmet", "Yılmaz", null, "Wohnung 6", 450m, 140m, "DE27100777770209299700", null),
            new Tenant("Lisa", "Schneider-Weber", null, "Wohnung 7", 650m, 160m, "DE44500105175407324931", null),
            new Tenant("Jürgen", "Köhler", null, "Wohnung 8", 540m, 140m, "DE12500105170648489890", "Köhler Elektrotechnik"),
        };

        var months = new[] { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
        var random = new Random(20250101);
        var leases = new List<LeaseCandidate>();
        foreach (var tenant in tenants)
        {
            var charges = Enumerable.Range(1, 12).SelectMany(m => new[]
            {
                Rent(2025, m, tenant.Base),
                Rent(2025, m, tenant.Utilities, "Nebenkosten"),
            }).ToList();
            leases.Add(new LeaseCandidate(Guid.CreateVersion7(), $"Haus, {tenant.Unit}", tenant.Unit, new DateOnly(2024, 1, 1), null,
                [new TenantInfo(tenant.First.Length == 0 ? null : tenant.First, tenant.Last, tenant.Company, [tenant.Iban])],
                _ => tenant.Base + tenant.Utilities, charges));
        }

        var transactions = new List<MatchInput>();
        var expected = new List<Guid>();
        for (var month = 1; month <= 12; month++)
        {
            for (var t = 0; t < tenants.Length; t++)
            {
                var tenant = tenants[t];
                var total = tenant.Base + tenant.Utilities;
                var yy = (2025 % 100).ToString("00", CultureInfo.InvariantCulture);
                var mm = month.ToString("00", CultureInfo.InvariantCulture);

                // Tenant 3 pays January and February together in February (collective payment); in January nothing arrives.
                if (t == 2 && month == 1)
                {
                    continue;
                }

                var amount = t == 2 && month == 2 ? 2 * total : total;
                if (t == 4 && month == 6)
                {
                    amount = total - 50m; // partial payment
                }

                var variant = random.Next(100);
                var payer = tenant.Last;
                var name = (variant % 6) switch
                {
                    0 => $"{tenant.Last.ToUpperInvariant()}, {tenant.First.ToUpperInvariant()}".Trim(',', ' '),
                    1 => $"{tenant.First} {tenant.Last}".Trim(),
                    2 => $"{tenant.Last} {tenant.First}".Trim(),
                    3 => $"{(tenant.First.Length > 0 ? tenant.First[..1] + ". " : "")}{tenant.Last}",
                    4 => $"{tenant.First} {tenant.Last.Replace("mann", "mamn", StringComparison.Ordinal)}".Trim(),
                    _ => tenant.Last.ToUpperInvariant(),
                };

                var otherPayer = tenant.OtherPayer is not null && random.Next(100) < 12;
                if (otherPayer)
                {
                    name = tenant.OtherPayer!;
                }

                var iban = random.Next(100) switch
                {
                    < 78 => tenant.Iban,
                    < 90 => null,
                    _ => IbanB,
                };
                if (otherPayer)
                {
                    iban = IbanB;
                }

                var purpose = random.Next(8) switch
                {
                    0 => $"Miete {mm}/{yy}",
                    1 => $"Miete {months[month - 1]} 2025 {tenant.Unit}",
                    2 => $"{tenant.Unit} Miete und Nebenkosten {mm}/2025",
                    3 => $"Mietzahlung {months[month - 1]}",
                    4 => $"Miete {tenant.Last} {mm}.2025",
                    5 => $"Whg {tenant.Unit[^1]} Miete {mm}/{yy}",
                    6 => "Dauerauftrag Miete",
                    _ => $"{tenant.Unit} {months[month - 1]} 2025",
                };

                if (t == 2 && month == 2)
                {
                    purpose = "Miete Januar und Februar 2025";
                }

                transactions.Add(new MatchInput(new DateOnly(2025, month, 2 + random.Next(4)), amount, name, iban, purpose));
                expected.Add(leases[t].LeaseId);
            }
        }

        return (transactions, expected, leases);
    }
}
