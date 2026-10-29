using Elocaro.Application.Common;
using Elocaro.Application.Tax;
using Elocaro.Domain.Common;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Receipts;
using Elocaro.Domain.Tax;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Finance;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Tax;

internal sealed class AnlageVService(
    ElocaroDbContext db,
    IDepreciationService depreciation,
    ILoanService loans,
    IMaintenanceService maintenance) : IAnlageVService
{
    public Task<AnlageVReport?> GetAsync(Guid propertyId, int year, bool surplusForecastPositive = false, CancellationToken cancellationToken = default) =>
        BuildAsync(propertyId, year, surplusForecastPositive, null, cancellationToken);

    public Task<AnlageVReport?> GetForOwnerAsync(Guid propertyId, int year, Guid ownerPersonId, bool surplusForecastPositive = false, CancellationToken cancellationToken = default) =>
        BuildAsync(propertyId, year, surplusForecastPositive, ownerPersonId, cancellationToken);

    private async Task<AnlageVReport?> BuildAsync(Guid propertyId, int year, bool surplusForecastPositive, Guid? ownerPersonId, CancellationToken cancellationToken)
    {
        var name = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken);
        if (name is null)
        {
            return null;
        }

        var postings = new List<AnlageVPosting>();
        var warnings = new List<string>();
        postings.AddRange(await IncomeAsync(propertyId, year, cancellationToken));
        postings.AddRange(await ReceiptsAsync(propertyId, year, warnings, cancellationToken));

        var yearEnd = new DateOnly(year, 12, 31);
        foreach (var portion in (await maintenance.DeductionAsync(propertyId, year, cancellationToken)).Portions)
        {
            postings.Add(new AnlageVPosting(
                AnlageVCategory.Maintenance,
                yearEnd,
                portion.Distributed ? $"{portion.Description} (Verteilungsbetrag)" : portion.Description,
                portion.Amount,
                AnlageVSource.Receipt,
                portion.ReceiptId));
        }

        var plan = (await depreciation.GetAsync(propertyId, cancellationToken))?.Plan;
        if (plan?.Years.FirstOrDefault(y => y.Year == year) is { } depreciationYear && depreciationYear.Total != 0)
        {
            postings.Add(new AnlageVPosting(AnlageVCategory.Depreciation, yearEnd, "AfA laut Abschreibungsplan", depreciationYear.Total, AnlageVSource.Depreciation, propertyId));
        }
        else if (plan is null)
        {
            warnings.Add("Für das Objekt sind keine Anschaffungsdaten erfasst; die AfA fehlt in der Auswertung.");
        }

        var financing = await loans.ExpensesAsync(propertyId, year, cancellationToken);
        if (financing.Interest != 0)
        {
            postings.Add(new AnlageVPosting(AnlageVCategory.Interest, yearEnd, "Darlehenszinsen (anteilig Vermietung)", financing.Interest, AnlageVSource.Loan, propertyId));
        }

        if (financing.OneOffCosts != 0)
        {
            postings.Add(new AnlageVPosting(AnlageVCategory.FinancingCosts, yearEnd, "Finanzierungskosten (Einmalkosten)", financing.OneOffCosts, AnlageVSource.Loan, propertyId));
        }

        var lines = (await GetMappingAsync(year, cancellationToken)).ToDictionary(m => m.Category, m => m.LineNumber);
        var localRent = await db.Set<LocalRent>().AsNoTracking().Where(m => m.PropertyId == propertyId && m.TaxYear == year).Select(m => (decimal?)m.AnnualRent).SingleOrDefaultAsync(cancellationToken);
        var rent = postings.Where(p => p.Category is AnlageVCategory.BaseRent or AnlageVCategory.AllocatedCosts).Sum(p => p.Amount);
        decimal? percent = localRent is { } reference ? Math.Round(rent / reference * 100m, 1, MidpointRounding.AwayFromZero) : null;
        if (AnlageVCalculator.ReducedRentHint(percent, surplusForecastPositive) is { } hint)
        {
            warnings.Add(hint);
        }

        var share = AnlageVCalculator.DeductibleShare(percent, surplusForecastPositive);
        if (ownerPersonId is not { } ownerId)
        {
            return new AnlageVReport(propertyId, name, year, AnlageVCalculator.Calculate(postings, lines, share), warnings, localRent, percent);
        }

        // The cheap-letting test above looks at the whole property; the owner's amounts are the shares of the totals.
        var owners = await db.Set<PropertyOwner>().AsNoTracking().Where(o => o.PropertyId == propertyId).ToListAsync(cancellationToken);
        var ownerShare = PropertyOwner.YearShare(owners, ownerId, year);
        if (ownerShare == 0m)
        {
            return null;
        }

        var fraction = ownerShare / 100m;
        var scaled = postings.Select(p => p with { Amount = Math.Round(p.Amount * fraction, 2, MidpointRounding.AwayFromZero) }).Where(p => p.Amount != 0m).ToList();
        warnings.Insert(0, $"Anteil des Eigentümers: {ownerShare:0.####} % aller Beträge des Objekts. Die Beträge sind je Buchung auf den Cent gerundet; die Summe aller Eigentümer kann um wenige Cent vom Gesamtwert abweichen.");
        if (owners.Where(o => o.PersonId == ownerId).Any(o => (o.ValidFrom is { } f && f.Year == year && f > new DateOnly(year, 1, 1)) || (o.ValidTo is { } t && t.Year == year && t < new DateOnly(year, 12, 31))))
        {
            warnings.Add("Der Anteil hat sich im Jahr geändert; angesetzt ist der nach Tagen gewichtete Durchschnitt. Ob stattdessen die Beträge nach dem Zeitpunkt aufzuteilen sind, klärt der Steuerberater.");
        }

        var ownerName = await db.Set<Elocaro.Domain.People.Person>().AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.DisplayName).SingleAsync(cancellationToken);
        return new AnlageVReport(propertyId, name, year, AnlageVCalculator.Calculate(scaled, lines, share), warnings, localRent is { } fullRent ? Math.Round(fullRent * fraction, 2, MidpointRounding.AwayFromZero) : null, percent)
        {
            OwnerName = ownerName,
            OwnerSharePercent = ownerShare,
        };
    }

    public Task<Result> SaveLocalRentAsync(Guid propertyId, int year, decimal? annualRent, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var stored = await db.Set<LocalRent>().SingleOrDefaultAsync(m => m.PropertyId == propertyId && m.TaxYear == year, cancellationToken);
            if (annualRent is null)
            {
                if (stored is not null)
                {
                    db.Remove(stored);
                }
            }
            else if (stored is null)
            {
                db.Add(new LocalRent(propertyId, year, annualRent.Value));
            }
            else
            {
                stored.SetRent(annualRent.Value);
            }

            await db.SaveChangesAsync(cancellationToken);
            return propertyId;
        });

    public async Task<IReadOnlyList<LineMappingRow>> GetMappingAsync(int year, CancellationToken cancellationToken = default)
    {
        var stored = await db.Set<TaxLineMapping>().AsNoTracking().Where(m => m.TaxYear == year).ToDictionaryAsync(m => m.Category, m => m.LineNumber, cancellationToken);
        return AnlageVCategories.All
            .Select(c => new LineMappingRow(c.Category, c.Section, c.Label, stored.GetValueOrDefault(c.Category, c.DefaultLine), !stored.ContainsKey(c.Category)))
            .ToList();
    }

    public Task<Result> SaveMappingAsync(int year, IReadOnlyDictionary<AnlageVCategory, int> lines, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(lines);
            if (year is < 2000 or > 2100)
            {
                throw new RuleViolationException("Bitte ein gültiges Steuerjahr angeben.");
            }

            var stored = await db.Set<TaxLineMapping>().Where(m => m.TaxYear == year).ToDictionaryAsync(m => m.Category, cancellationToken);
            foreach (var (category, line) in lines)
            {
                if (stored.TryGetValue(category, out var mapping))
                {
                    mapping.SetLine(line);
                }
                else
                {
                    db.Add(new TaxLineMapping(year, category, line));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Guid.Empty;
        });

    /// <summary>Payments of the tenants split by the receivables they settle; refunds paid out reduce the income.</summary>
    private async Task<List<AnlageVPosting>> IncomeAsync(Guid propertyId, int year, CancellationToken cancellationToken)
    {
        var units = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).SelectMany(o => o.Units).Select(e => e.Id).ToListAsync(cancellationToken);
        var rooms = await db.Set<Property>().AsNoTracking().Where(o => o.Id == propertyId).SelectMany(o => o.Units).SelectMany(e => e.Room).Select(z => z.Id).ToListAsync(cancellationToken);
        var rentalUnits = units.Concat(rooms).ToList();
        var leaseIds = await db.Set<Lease>().AsNoTracking().Where(v => rentalUnits.Contains(v.RentalUnitId)).Select(v => v.Id).ToListAsync(cancellationToken);
        var settings = await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken);
        var charges = (await db.Set<Charge>().AsNoTracking().Where(s => leaseIds.Contains(s.LeaseId)).ToListAsync(cancellationToken)).ToLookup(s => s.LeaseId);
        var payments = (await db.Set<Payment>().AsNoTracking().Where(z => leaseIds.Contains(z.LeaseId)).ToListAsync(cancellationToken)).ToLookup(z => z.LeaseId);

        var postings = new List<AnlageVPosting>();
        foreach (var leaseId in leaseIds)
        {
            var leaseCharges = charges[leaseId].ToList();
            var leasePayments = payments[leaseId].ToList();
            var chargeById = leaseCharges.ToDictionary(s => s.Id);
            var positive = leasePayments.Where(z => z.Amount > 0).ToDictionary(z => z.Id);
            var result = TenantAccounts.Allocate(leaseCharges, leasePayments, settings.Allocation);
            foreach (var share in result.Shares.Where(a => positive.ContainsKey(a.CreditId) && chargeById.ContainsKey(a.ClaimId)))
            {
                var charge = chargeById[share.ClaimId];
                if (AnlageVCategories.ForCharge(charge.Kind, charge.Component) is not { } category)
                {
                    continue;
                }

                if (CashBasisYear.YearOf(share.Date, charge.Due, charge.Automatic) == year)
                {
                    postings.Add(new AnlageVPosting(category, share.Date, charge.Name, share.Amount, AnlageVSource.Payment, share.CreditId, leaseId));
                }
            }

            foreach (var payout in leasePayments.Where(z => z.Amount < 0 && z.Date.Year == year))
            {
                postings.Add(new AnlageVPosting(AnlageVCategory.Refunds, payout.Date, payout.Purpose ?? "Auszahlung an Mieter", payout.Amount, AnlageVSource.Payment, payout.Id, leaseId));
            }
        }

        return postings;
    }

    private async Task<List<AnlageVPosting>> ReceiptsAsync(Guid propertyId, int year, List<string> warnings, CancellationToken cancellationToken)
    {
        var rows = await db.Set<Receipt>().AsNoTracking()
            .SelectMany(b => b.Items.Where(p => p.PropertyId == propertyId), (b, p) => new
            {
                ReceiptId = b.Id,
                b.SupplierName,
                b.InvoiceNumber,
                b.InvoiceDate,
                b.PaidOn,
                b.DueOn,
                p.CostType,
                p.Amount,
            })
            .ToListAsync(cancellationToken);

        var unpaid = rows.Where(r => r.PaidOn is null && r.InvoiceDate.Year == year && AnlageVCategories.ForCostType(r.CostType) is not null).Select(r => r.ReceiptId).Distinct().Count();
        if (unpaid > 0)
        {
            warnings.Add($"{unpaid} Beleg(e) aus {year} sind noch nicht bezahlt und fehlen deshalb (Zuflussprinzip). Bitte das Zahlungsdatum erfassen.");
        }

        var postings = new List<AnlageVPosting>();
        foreach (var row in rows)
        {
            if (row.PaidOn is not { } paidOn || AnlageVCategories.ForCostType(row.CostType) is not { } category)
            {
                continue;
            }

            if (CashBasisYear.YearOf(paidOn, row.DueOn, AnlageVCategories.IsRecurring(category)) == year)
            {
                var text = string.Join(" – ", new[] { row.SupplierName, row.InvoiceNumber, CostTypes.Find(row.CostType)?.Name }.Where(t => !string.IsNullOrWhiteSpace(t)));
                postings.Add(new AnlageVPosting(category, paidOn, text, row.Amount, AnlageVSource.Receipt, row.ReceiptId));
            }
        }

        return postings;
    }
}
