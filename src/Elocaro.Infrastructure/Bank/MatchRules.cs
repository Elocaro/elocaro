using Elocaro.Application.Bank;
using Elocaro.Application.Common;
using Elocaro.Domain.Bank;
using Elocaro.Domain.Common;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Bank;

internal sealed class MatchRules(ElocaroDbContext db) : IMatchRules
{
    public async Task<IReadOnlyList<MatchRuleRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.Set<MatchRule>().AsNoTracking().OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        return rules.Select(r => new MatchRuleRow(r.Id, r.Name, r.Priority, Condition(r), Target(r, properties), r.Action)).ToList();
    }

    public async Task<MatchRuleInput?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.Set<MatchRule>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rule is null ? null : new MatchRuleInput
        {
            Name = rule.Name,
            Priority = rule.Priority,
            Direction = rule.Direction,
            CounterpartyIban = rule.CounterpartyIban,
            CounterpartyContains = rule.CounterpartyContains,
            PurposeContains = rule.PurposeContains,
            Action = rule.Action,
            CostType = rule.CostType,
            PropertyId = rule.PropertyId,
            UnitId = rule.UnitId,
            Allocable = rule.Allocable,
            LeaseId = rule.LeaseId,
        };
    }

    public Task<Result> SaveAsync(Guid? id, MatchRuleInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var rule = id is { } existing
                ? await db.Set<MatchRule>().SingleOrDefaultAsync(r => r.Id == existing, cancellationToken) ?? throw new NotFoundException()
                : new MatchRule(data.Name, data.Action);
            rule.Name = data.Name.Trim();
            rule.Priority = data.Priority;
            rule.Direction = data.Direction;
            rule.SetCounterpartyIban(data.CounterpartyIban);
            rule.CounterpartyContains = TrimToNull(data.CounterpartyContains);
            rule.PurposeContains = TrimToNull(data.PurposeContains);
            rule.Action = data.Action;
            rule.CostType = TrimToNull(data.CostType);
            rule.PropertyId = data.PropertyId;
            rule.UnitId = data.UnitId;
            rule.Allocable = data.Allocable;
            rule.LeaseId = data.LeaseId;
            rule.Validate();

            if (rule.PropertyId is { } property && !await db.Set<Property>().AnyAsync(o => o.Id == property, cancellationToken))
            {
                throw new RuleViolationException("Das Objekt wurde nicht gefunden.");
            }

            if (rule.UnitId is { } unit && !await db.Set<Unit>().AnyAsync(e => e.Id == unit && (rule.PropertyId == null || e.PropertyId == rule.PropertyId), cancellationToken))
            {
                throw new RuleViolationException("Die Einheit gehört nicht zum gewählten Objekt.");
            }

            if (rule.LeaseId is { } lease && !await db.Set<Domain.Leases.Lease>().AnyAsync(v => v.Id == lease, cancellationToken))
            {
                throw new RuleViolationException("Das Mietverhältnis wurde nicht gefunden.");
            }

            if (id is null)
            {
                db.Add(rule);
            }

            await db.SaveChangesAsync(cancellationToken);
            return rule.Id;
        });

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var rule = await db.Set<MatchRule>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(rule);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static string Condition(MatchRule r)
    {
        var parts = new List<string>();
        if (r.Direction != RuleDirection.Any)
        {
            parts.Add(r.Direction == RuleDirection.Income ? "Eingang" : "Ausgabe");
        }

        if (r.CounterpartyIban is not null)
        {
            parts.Add($"Gegen-IBAN {Iban.Format(r.CounterpartyIban)}");
        }

        if (r.CounterpartyContains is not null)
        {
            parts.Add($"Name enthält „{r.CounterpartyContains}“");
        }

        if (r.PurposeContains is not null)
        {
            parts.Add($"Zweck enthält „{r.PurposeContains}“");
        }

        return string.Join(", ", parts);
    }

    private static string Target(MatchRule r, Dictionary<Guid, string> properties) => r.Action switch
    {
        RuleAction.Ignore => "Ignorieren (privat)",
        RuleAction.Lease => "Miete eines Mietverhältnisses",
        _ => $"{CostTypes.Find(r.CostType ?? "")?.Name ?? r.CostType}{(r.PropertyId is { } p ? ", " + properties.GetValueOrDefault(p, "?") : "")}",
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
