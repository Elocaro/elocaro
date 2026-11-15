using System.Globalization;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Payments;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Payments;

/// <summary>
/// Collects the open monthly rents of the leases paid by direct debit from the tenant accounts and builds the SEPA file with <see cref="DirectDebitFile"/>.
/// The amount is what the ledger still shows as open for the rent, so nothing is collected twice that was already paid.
/// </summary>
internal sealed class DirectDebits(
    ElocaroDbContext db,
    ITenantAccounts accounts,
    ILeaseService leases,
    ILandlordProfileService profiles,
    IAccessLog accessLog,
    TimeProvider clock) : IDirectDebits
{
    /// <summary>How far ahead rents are offered.</summary>
    private const int HorizonDays = 45;

    private sealed record Item(DebitCandidate Candidate, DirectDebitMandate? Mandate);

    public async Task<IReadOnlyList<DebitCandidate>> CandidatesAsync(CancellationToken cancellationToken = default) =>
        [.. (await AllAsync(cancellationToken)).Select(i => i.Candidate)];

    public async Task<PaymentOrderResult> CreateFileAsync(IReadOnlyList<string> keys, DateOnly collectionDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var now = clock.GetUtcNow();
        if (collectionDate <= GermanTime.Today(now))
        {
            return PaymentOrderResult.Failed("Der Einzugstag muss mindestens einen Tag in der Zukunft liegen: Die Bank braucht die Datei vor dem Einzug.");
        }

        var wanted = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return PaymentOrderResult.Failed("Es ist kein Einzug ausgewählt.");
        }

        var all = (await AllAsync(cancellationToken)).ToDictionary(i => i.Candidate.Key, StringComparer.Ordinal);
        var chosen = new List<Item>();
        foreach (var key in wanted)
        {
            if (!all.TryGetValue(key, out var item))
            {
                return PaymentOrderResult.Failed("Ein ausgewählter Einzug ist nicht mehr offen. Bitte die Liste neu laden.");
            }

            if (item.Candidate.Problem is { } problem)
            {
                return PaymentOrderResult.Failed($"{item.Candidate.Tenant}: {problem}");
            }

            if (item.Candidate.Due > collectionDate)
            {
                return PaymentOrderResult.Failed($"{item.Candidate.Tenant}: Die Miete ist erst am {item.Candidate.Due:dd.MM.yyyy} fällig, der Einzugstag liegt davor.");
            }

            chosen.Add(item);
        }

        var profile = await profiles.GetAsync(cancellationToken);
        if (profile is null)
        {
            return PaymentOrderResult.Failed("Das Vermieterprofil fehlt: Name, IBAN und Gläubiger-ID des Kontos, auf das eingezogen wird, stehen dort.");
        }

        var debits = chosen.Select(i => new DirectDebit(
            "ELO-" + Guid.NewGuid().ToString("N")[..24],
            i.Candidate.Amount,
            i.Mandate!.Reference,
            i.Mandate.SignedOn,
            i.Mandate.DebtorName,
            i.Mandate.DebtorIban,
            i.Mandate.DebtorBic,
            $"Miete {i.Candidate.Due.ToString("MM/yyyy", CultureInfo.InvariantCulture)} {i.Candidate.RentalUnit}",
            i.Mandate.Sequence)).ToList();
        byte[] file;
        try
        {
            file = DirectDebitFile.Build(new DirectDebitBatch(
                "ELOCARO-" + now.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), now, collectionDate, profile.Name, profile.Iban ?? "", profile.Bic, profile.CreditorId ?? "", debits));
        }
        catch (RuleViolationException ex)
        {
            return PaymentOrderResult.Failed(ex.Message);
        }

        foreach (var mandate in chosen.Select(i => i.Mandate!.Id).Distinct())
        {
            await accessLog.LogAsync(nameof(DirectDebitMandate), mandate, "IBAN für Lastschriftdatei", cancellationToken);
        }

        return new PaymentOrderResult(true, [], $"lastschriften-{collectionDate:yyyy-MM-dd}.xml", file, chosen.Count, chosen.Sum(i => i.Candidate.Amount));
    }

    private async Task<List<Item>> AllAsync(CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var horizon = today.AddDays(HorizonDays);
        var mandates = await db.Set<DirectDebitMandate>().AsNoTracking().ToListAsync(cancellationToken);
        var debitLeases = await db.Set<Lease>().AsNoTracking().Where(l => l.PaymentMethod == PaymentMethod.DirectDebit).Select(l => l.Id).ToListAsync(cancellationToken);
        var leaseIds = debitLeases.Concat(mandates.Where(m => m.IsActiveOn(horizon) || m.IsActiveOn(today)).Select(m => m.LeaseId)).Distinct().ToList();
        var names = (await leases.ListAsync(false, cancellationToken)).ToDictionary(l => l.Id);
        var result = new List<Item>();
        foreach (var leaseId in leaseIds)
        {
            if (!names.TryGetValue(leaseId, out var lease) || await accounts.GetAsync(leaseId, cancellationToken) is not { } account)
            {
                continue;
            }

            var rents = account.Entries.Where(e => e.EntryType == EntryType.Charge && e.Kind == ChargeType.Rent && e.Open is > 0m && e.Date <= horizon).GroupBy(e => e.Date);
            foreach (var group in rents.OrderBy(g => g.Key))
            {
                var mandate = mandates.Where(m => m.LeaseId == leaseId && m.IsActiveOn(group.Key)).OrderByDescending(m => m.SignedOn).FirstOrDefault();
                var problem = mandate is null ? "Es gibt kein gültiges SEPA-Mandat für den Fälligkeitstag (Geld → Bank → Lastschrift-Mandate)." : null;
                result.Add(new Item(
                    new DebitCandidate($"debit:{leaseId}:{group.Key:yyyy-MM-dd}", leaseId, lease.Tenant, lease.RentalUnit, mandate?.Reference, group.Sum(e => e.Open!.Value), group.Key, problem), mandate));
            }
        }

        return [.. result.OrderBy(i => i.Candidate.Due).ThenBy(i => i.Candidate.Tenant, StringComparer.CurrentCultureIgnoreCase)];
    }
}
