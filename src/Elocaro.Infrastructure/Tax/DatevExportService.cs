using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elocaro.Application.Common;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Datev;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Finance;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Tax;

internal sealed class DatevExportService(ElocaroDbContext db, TimeProvider clock) : IDatevExport
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<DatevSettingsInput> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var s = await db.Set<DatevSettings>().AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken) ?? new DatevSettings();
        return new DatevSettingsInput
        {
            ConsultantNumber = s.ConsultantNumber,
            ClientNumber = s.ClientNumber,
            Chart = s.Chart,
            AccountLength = s.AccountLength,
            FiscalYearStartMonth = s.FiscalYearStartMonth,
            DictationCode = s.DictationCode,
        };
    }

    public Task<Result> SaveSettingsAsync(DatevSettingsInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var settings = await db.Set<DatevSettings>().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new DatevSettings();
                db.Add(settings);
            }

            settings.Set(input.ConsultantNumber, input.ClientNumber, input.Chart, input.AccountLength, input.FiscalYearStartMonth, input.DictationCode);
            await db.SaveChangesAsync(cancellationToken);
            return settings.Id;
        });

    public async Task<IReadOnlyList<DatevAccountRow>> AccountsAsync(CancellationToken cancellationToken = default)
    {
        var chart = (await GetSettingsAsync(cancellationToken)).Chart;
        var custom = await db.Set<DatevAccount>().AsNoTracking().ToDictionaryAsync(a => a.Key, a => a.Account, cancellationToken);
        return DatevAccountKeys.All.Select(key =>
        {
            var proposal = DatevAccountKeys.Default(chart, key);
            return new DatevAccountRow(key, DatevAccountKeys.Label(key), custom.GetValueOrDefault(key, proposal), !custom.ContainsKey(key));
        }).ToList();
    }

    public Task<Result> SaveAccountsAsync(IReadOnlyDictionary<string, int> accounts, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(accounts);
            var chart = (await GetSettingsAsync(cancellationToken)).Chart;
            var existing = await db.Set<DatevAccount>().ToDictionaryAsync(a => a.Key, cancellationToken);
            foreach (var (key, account) in accounts)
            {
                if (!DatevAccountKeys.All.Contains(key))
                {
                    throw new RuleViolationException("Unbekannter Schlüssel für das Konto.");
                }

                var isProposal = account == DatevAccountKeys.Default(chart, key);
                if (existing.TryGetValue(key, out var row))
                {
                    if (isProposal)
                    {
                        db.Remove(row);
                    }
                    else
                    {
                        row.SetAccount(account);
                    }
                }
                else if (!isProposal)
                {
                    db.Add(new DatevAccount(key, account));
                }
                else if (account is < 1 or > 99_999_999)
                {
                    throw new RuleViolationException("Ein Sachkonto hat 1 bis 8 Ziffern.");
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Guid.Empty;
        });

    public Task<Result> ResetAccountsAsync(CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            db.RemoveRange(await db.Set<DatevAccount>().ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            return Guid.Empty;
        });

    public async Task<DatevPreview> PreviewAsync(DateOnly from, DateOnly to, bool includeExported, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var (all, warnings) = await BuildAsync(from, to, cancellationToken);
        var exported = (await db.Set<DatevExportedItem>().AsNoTracking().Select(i => i.SourceKey).ToListAsync(cancellationToken)).ToHashSet();
        var skipped = includeExported ? [] : all.Where(b => exported.Contains(b.SourceKey)).ToList();
        return new DatevPreview(all.Except(skipped).ToList(), warnings, skipped.Count, settings.ConsultantNumber > 0 && settings.ClientNumber > 0);
    }

    public Task<Result> ExportAsync(DateOnly from, DateOnly to, bool fixedPosting, bool includeExported, string exportedBy, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var settings = await db.Set<DatevSettings>().AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (settings is not { IsComplete: true })
            {
                throw new RuleViolationException("Bitte zuerst Berater- und Mandantennummer eintragen.");
            }

            var fiscalYearStart = FiscalYearStart(from, settings.FiscalYearStartMonth);
            if (to > fiscalYearStart.AddYears(1).AddDays(-1))
            {
                throw new RuleViolationException($"Der Zeitraum darf nicht über das Wirtschaftsjahr ({fiscalYearStart:dd.MM.yyyy} bis {fiscalYearStart.AddYears(1).AddDays(-1):dd.MM.yyyy}) hinausgehen.");
            }

            var preview = await PreviewAsync(from, to, includeExported, cancellationToken);
            if (preview.Bookings.Count == 0)
            {
                throw new RuleViolationException(preview.AlreadyExported > 0 ? "Alle Buchungen des Zeitraums sind bereits exportiert." : "Im Zeitraum gibt es nichts zu exportieren.");
            }

            var now = clock.GetUtcNow();
            var header = new DatevHeader(settings.ConsultantNumber, settings.ClientNumber, fiscalYearStart, settings.AccountLength, from, to,
                $"Elocaro {from:yyyyMMdd}-{to:yyyyMMdd}", settings.DictationCode, fixedPosting, settings.Chart, now, exportedBy);
            var content = DatevFormat.Write(header, preview.Bookings);
            var name = $"EXTF_Buchungsstapel_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
            var export = new DatevExport(from, to, preview.Bookings.Count, preview.Total, fixedPosting, name, content, Convert.ToHexString(SHA256.HashData(content)));
            db.Add(export);
            var known = (await db.Set<DatevExportedItem>().Select(i => i.SourceKey).ToListAsync(cancellationToken)).ToHashSet();
            foreach (var key in preview.Bookings.Select(b => b.SourceKey).Where(k => !known.Contains(k)))
            {
                db.Add(new DatevExportedItem(export.Id, key));
            }

            await db.SaveChangesAsync(cancellationToken);
            return export.Id;
        });

    public async Task<IReadOnlyList<DatevExportRow>> ExportsAsync(CancellationToken cancellationToken = default) =>
        (await db.Set<DatevExport>().AsNoTracking().OrderByDescending(e => e.CreatedAt)
            .Select(e => new { e.Id, e.CreatedAt, e.CreatedBy, e.From, e.To, e.Count, e.Total, e.FixedPosting, e.FileName }).ToListAsync(cancellationToken))
        .Select(e => new DatevExportRow(e.Id, e.CreatedAt, e.CreatedBy ?? "", e.From, e.To, e.Count, e.Total, e.FixedPosting, e.FileName)).ToList();

    public async Task<DatevFile?> FileAsync(Guid exportId, CancellationToken cancellationToken = default)
    {
        var export = await db.Set<DatevExport>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == exportId, cancellationToken);
        return export is null ? null : new DatevFile(export.FileName, export.Content);
    }

    public async Task<byte[]> BookingsCsvAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var (bookings, _) = await BuildAsync(from, to, cancellationToken);
        var text = new StringBuilder("Datum;Soll/Haben;Konto;Gegenkonto;Betrag;Belegnummer;Text\r\n");
        foreach (var b in bookings)
        {
            text.AppendJoin(';', b.Date.ToString("dd.MM.yyyy", German), b.Debit ? "S" : "H", b.Account.ToString(German), b.CounterAccount.ToString(German),
                b.Amount.ToString("0.00", German), Field(b.DocumentNumber), Field(b.Text)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    internal static DateOnly FiscalYearStart(DateOnly day, int startMonth) => new(day.Month >= startMonth ? day.Year : day.Year - 1, startMonth, 1);

    /// <summary>Quotes where needed and neutralizes formulas (CSV injection).</summary>
    private static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    /// <summary>Cash-basis bookings of the period: payments received (split by the claims they settle), payouts and paid cost items.</summary>
    private async Task<(List<DatevBooking> Bookings, List<string> Warnings)> BuildAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var accounts = (await AccountsAsync(cancellationToken)).ToDictionary(a => a.Key, a => a.Account);
        var bank = accounts[DatevAccountKeys.Bank];
        var bookings = new List<DatevBooking>();
        var warnings = new List<string>();

        var financeSettings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var charges = (await db.Set<Charge>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(c => c.LeaseId);
        var payments = (await db.Set<Payment>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(p => p.LeaseId);
        var withoutClaim = 0;
        foreach (var leaseId in payments.Select(g => g.Key))
        {
            var leaseCharges = charges[leaseId].ToList();
            var leasePayments = payments[leaseId].ToList();
            var chargeById = leaseCharges.ToDictionary(c => c.Id);
            var result = TenantAccounts.Allocate(leaseCharges, leasePayments, financeSettings.Allocation);
            var positive = leasePayments.Where(p => p.Amount > 0).Select(p => p.Id).ToHashSet();
            foreach (var share in result.Shares.Where(a => positive.Contains(a.CreditId) && chargeById.ContainsKey(a.ClaimId) && a.Date >= from && a.Date <= to))
            {
                var charge = chargeById[share.ClaimId];
                if (AnlageVCategories.ForCharge(charge.Kind, charge.Component) is not { } category)
                {
                    continue; // deposits and balances brought forward are no income
                }

                bookings.Add(new DatevBooking(share.Date, share.Amount, true, bank, accounts[DatevAccountKeys.Income(category)], "", charge.Name, $"payment:{share.CreditId}:{share.ClaimId}"));
            }

            foreach (var payout in leasePayments.Where(p => p.Amount < 0 && p.Date >= from && p.Date <= to))
            {
                bookings.Add(new DatevBooking(payout.Date, -payout.Amount, false, bank, accounts[DatevAccountKeys.Income(AnlageVCategory.Refunds)], "", payout.Purpose ?? "Auszahlung an Mieter", $"payout:{payout.Id}"));
            }

            if (result.Overpayment > 0)
            {
                withoutClaim++;
            }
        }

        if (withoutClaim > 0)
        {
            warnings.Add($"{withoutClaim} Mietverhältnis(se) haben Zahlungen ohne passende Forderung (Vorauszahlung oder Überzahlung). Diese Beträge fehlen im Export, bis die Forderung entsteht.");
        }

        var receipts = await db.Set<Receipt>().AsNoTracking().Include(r => r.Items).ToListAsync(cancellationToken);
        var unpaid = 0;
        var unallocated = 0;
        foreach (var receipt in receipts)
        {
            if (receipt.PaidOn is not { } paidOn)
            {
                unpaid += receipt.InvoiceDate >= from && receipt.InvoiceDate <= to ? 1 : 0;
                continue;
            }

            if (paidOn < from || paidOn > to)
            {
                continue;
            }

            if (!receipt.IsFullyAllocated)
            {
                unallocated++;
            }

            foreach (var item in receipt.Items.Where(i => i.CostType != "leerstand"))
            {
                var text = string.Join(" – ", new[] { receipt.SupplierName, CostTypes.Find(item.CostType)?.Name }.Where(t => !string.IsNullOrWhiteSpace(t)));
                bookings.Add(new DatevBooking(paidOn, Math.Abs(item.Amount), item.Amount > 0, accounts[DatevAccountKeys.Cost(item.CostType)], bank, receipt.InvoiceNumber ?? "", text, $"cost:{item.Id}"));
            }
        }

        if (unpaid > 0)
        {
            warnings.Add($"{unpaid} Beleg(e) mit Rechnungsdatum im Zeitraum haben noch kein Zahlungsdatum und fehlen (Zuflussprinzip).");
        }

        if (unallocated > 0)
        {
            warnings.Add($"{unallocated} bezahlte(r) Beleg(e) sind nicht vollständig auf Kostenarten aufgeteilt; nur die aufgeteilten Positionen werden exportiert.");
        }

        return (bookings.OrderBy(b => b.Date).ThenBy(b => b.SourceKey, StringComparer.Ordinal).ToList(), warnings);
    }
}
