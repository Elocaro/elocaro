using System.Globalization;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Application.Finance;
using Elocaro.Application.Leases;
using Elocaro.Application.Payments;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Payments;
using Elocaro.Domain.People;
using Elocaro.Domain.Receipts;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Payments;

/// <summary>
/// Collects what is still to be paid out (open receipts, credits of tenant accounts, deposits of ended leases) from the books and builds the SEPA file
/// with <see cref="CreditTransferFile"/>. Reads the data of other modules but changes none of it.
/// </summary>
internal sealed class PaymentOrders(
    ElocaroDbContext db,
    ITenantAccounts accounts,
    IDeposits deposits,
    ILandlordProfileService profiles,
    IAccessLog accessLog,
    TimeProvider clock) : IPaymentOrders
{
    private sealed record Item(PaymentCandidate Candidate, Guid? PersonId, string? Iban, string? HolderName);

    public async Task<IReadOnlyList<PaymentCandidate>> CandidatesAsync(CancellationToken cancellationToken = default) =>
        [.. (await AllAsync(cancellationToken)).Select(i => i.Candidate)];

    public async Task<PaymentOrderResult> CreateFileAsync(IReadOnlyList<string> keys, DateOnly executionDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var now = clock.GetUtcNow();
        if (executionDate < GermanTime.Today(now))
        {
            return PaymentOrderResult.Failed("Der Ausführungstag liegt in der Vergangenheit.");
        }

        var wanted = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return PaymentOrderResult.Failed("Es ist keine Überweisung ausgewählt.");
        }

        var all = (await AllAsync(cancellationToken)).ToDictionary(i => i.Candidate.Key, StringComparer.Ordinal);
        var chosen = new List<Item>();
        foreach (var key in wanted)
        {
            if (!all.TryGetValue(key, out var item))
            {
                return PaymentOrderResult.Failed("Eine ausgewählte Zahlung ist nicht mehr offen. Bitte die Liste neu laden.");
            }

            if (item.Candidate.Problem is { } problem)
            {
                return PaymentOrderResult.Failed($"{item.Candidate.Payee}: {problem}");
            }

            chosen.Add(item);
        }

        var profile = await profiles.GetAsync(cancellationToken);
        if (profile is null)
        {
            return PaymentOrderResult.Failed("Das Vermieterprofil fehlt: Name und IBAN des Kontos, von dem überwiesen wird, stehen dort.");
        }

        var transfers = chosen.Select(i => new CreditTransfer(
            "ELO-" + Guid.NewGuid().ToString("N")[..24], i.HolderName ?? i.Candidate.Payee, i.Iban!, i.Candidate.Amount, i.Candidate.Purpose)).ToList();
        byte[] file;
        try
        {
            file = CreditTransferFile.Build(new CreditTransferBatch(
                "ELOCARO-" + now.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), now, executionDate, profile.Name, profile.Iban ?? "", profile.Bic, transfers));
        }
        catch (RuleViolationException ex)
        {
            return PaymentOrderResult.Failed(ex.Message);
        }

        foreach (var personId in chosen.Select(i => i.PersonId).OfType<Guid>().Distinct())
        {
            await accessLog.LogAsync(nameof(Person), personId, "IBAN für Überweisungsdatei", cancellationToken);
        }

        var total = chosen.Sum(i => i.Candidate.Amount);
        return new PaymentOrderResult(true, [], $"ueberweisungen-{executionDate:yyyy-MM-dd}.xml", file, chosen.Count, total);
    }

    private async Task<List<Item>> AllAsync(CancellationToken cancellationToken)
    {
        var items = new List<Item>();
        items.AddRange(await ReceiptsAsync(cancellationToken));
        items.AddRange(await LeaseItemsAsync(cancellationToken));
        return [.. items.OrderBy(i => i.Candidate.DueOn ?? DateOnly.MaxValue).ThenBy(i => i.Candidate.Payee, StringComparer.CurrentCultureIgnoreCase)];
    }

    private async Task<List<Item>> ReceiptsAsync(CancellationToken cancellationToken)
    {
        var receipts = await db.Set<Receipt>().AsNoTracking().Where(r => r.PaidOn == null && r.Gross > 0).ToListAsync(cancellationToken);
        var supplierIds = receipts.Where(r => r.SupplierId != null).Select(r => r.SupplierId!.Value).Distinct().ToList();
        var suppliers = await db.Set<Person>().AsNoTracking().Include(p => p.BankDetails).Where(p => supplierIds.Contains(p.Id) && p.AnonymizedAt == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        var result = new List<Item>();
        foreach (var receipt in receipts)
        {
            suppliers.TryGetValue(receipt.SupplierId ?? Guid.Empty, out var supplier);
            var payee = supplier?.DisplayName ?? receipt.SupplierName ?? "Lieferant";
            var bank = supplier?.BankDetails.FirstOrDefault(b => !b.Deleted);
            var number = string.IsNullOrWhiteSpace(receipt.InvoiceNumber) ? $"vom {receipt.InvoiceDate:dd.MM.yyyy}" : receipt.InvoiceNumber;
            var problem = bank is null ? "Für den Lieferanten ist keine IBAN hinterlegt (Personen → Kontakt → Bankverbindung)." : null;
            result.Add(new Item(
                new PaymentCandidate($"receipt:{receipt.Id}", PaymentSource.Receipt, payee, $"Rechnung {number}", $"Rechnung {number}", receipt.Gross, receipt.DueOn, problem),
                supplier?.Id, bank?.Iban, bank?.AccountHolder));
        }

        return result;
    }

    private async Task<List<Item>> LeaseItemsAsync(CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).ToListAsync(cancellationToken);
        var personIds = leases.SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Include(p => p.BankDetails).Where(p => personIds.Contains(p.Id) && p.AnonymizedAt == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        var result = new List<Item>();
        foreach (var lease in leases)
        {
            var tenant = lease.Tenant.Select(p => people.GetValueOrDefault(p.PersonId)).FirstOrDefault(p => p is not null && p.BankDetails.Any(b => !b.Deleted)) ?? lease.Tenant.Select(p => people.GetValueOrDefault(p.PersonId)).FirstOrDefault(p => p is not null);
            var bank = tenant?.BankDetails.FirstOrDefault(b => !b.Deleted);
            var payee = tenant?.DisplayName ?? "Mieter";
            var problem = bank is null ? "Für den Mieter ist keine IBAN hinterlegt (Personen → Mieter → Bankverbindung)." : null;

            if (await accounts.GetAsync(lease.Id, cancellationToken) is { Balance: < 0m } account)
            {
                var credit = -account.Balance;
                result.Add(new Item(
                    new PaymentCandidate($"credit:{lease.Id}", PaymentSource.TenantCredit, payee, $"Guthaben {account.RentalUnit}", $"{account.RentalUnit}, Mieterkonto", credit, null, problem,
                        "Das Guthaben steht im Mieterkonto. Nach der Überweisung zeigt der Bankabgleich die Auszahlung; sie dann dem Mietverhältnis zuordnen."),
                    tenant?.Id, bank?.Iban, bank?.AccountHolder));
            }

            if (lease.End is { } end && end < today && await deposits.GetAsync(lease.Id, cancellationToken) is { } deposit && deposit.Account.Balance > 0m)
            {
                result.Add(new Item(
                    new PaymentCandidate($"deposit:{lease.Id}", PaymentSource.Deposit, payee, $"Kaution {deposit.RentalUnit}", $"{deposit.RentalUnit}, Kaution", deposit.Account.Balance, null, problem,
                        "Zuerst Verrechnungen (Nachzahlung, Schäden) unter Kaution erfassen: überwiesen wird der Saldo, der dort steht."),
                    tenant?.Id, bank?.Iban, bank?.AccountHolder));
            }
        }

        return result;
    }
}
