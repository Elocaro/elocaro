using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Tax;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Tax;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Tax;

internal sealed class LoanService(MietHahnDbContext db) : ILoanService
{
    public async Task<IReadOnlyList<LoanListItem>> ListAsync(CancellationToken cancellationToken = default) =>
        await (from d in db.Set<Loan>().AsNoTracking()
               join o in db.Set<Property>().AsNoTracking() on d.PropertyId equals o.Id
               orderby o.Name, d.DisbursementDate
               select new LoanListItem(d.Id, d.Bank, d.LoanNumber, d.PropertyId, o.Name, d.Principal, d.InterestRatePercent, d.FixedUntil))
            .ToListAsync(cancellationToken);

    public async Task<LoanDetail?> GetAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await db.Set<Loan>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == loanId, cancellationToken);
        if (loan is null)
        {
            return null;
        }

        var name = await db.Set<Property>().AsNoTracking().Where(o => o.Id == loan.PropertyId).Select(o => o.Name).SingleAsync(cancellationToken);
        var extras = await db.Set<LoanExtraPayment>().AsNoTracking().Where(s => s.LoanId == loanId).OrderBy(s => s.Date).ToListAsync(cancellationToken);
        var certificates = await db.Set<LoanCertificate>().AsNoTracking().Where(b => b.LoanId == loanId).OrderBy(b => b.Year).ToListAsync(cancellationToken);
        var costs = await db.Set<FinancingCost>().AsNoTracking().Where(k => k.LoanId == loanId).OrderBy(k => k.Date).ToListAsync(cancellationToken);

        var booked = await (from a in db.Set<MietHahn.Domain.Bank.TransactionAssignment>().AsNoTracking()
                            join u in db.Set<MietHahn.Domain.Bank.Transaction>().AsNoTracking() on a.TransactionId equals u.Id
                            where a.LoanId == loanId
                            orderby u.BookingDate
                            select new { u.Id, u.BookingDate, a.Amount, a.LoanInterest }).ToListAsync(cancellationToken);

        var plan = LoanCalculator.Calculate(loan.ToCalculation(), extras.Select(s => s.ToCalculation()).ToList());
        return new LoanDetail(
            loan.Id,
            name,
            ToInput(loan),
            plan,
            YearsOf(plan, certificates),
            extras.Select(s => new ExtraPaymentRow(s.Id, s.Date, s.Amount)).ToList(),
            certificates.Select(b => new CertificateRow(b.Id, b.Year, b.Interest, b.ClosingBalance)).ToList(),
            costs.Select(k => new FinancingCostRow(k.Id, k.Date, k.Amount, k.Kind, k.Description)).ToList())
        {
            Booked = booked.Select(b => new BookedInstalment(b.Id, b.BookingDate, -b.Amount, b.LoanInterest ?? 0m, -b.Amount - (b.LoanInterest ?? 0m))).ToList(),
        };
    }

    public Task<Result> CreateAsync(LoanInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var propertyId = input.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var loan = new Loan(propertyId, input.Bank, input.LoanNumber);
            Apply(loan, input);
            db.Add(loan);
            await db.SaveChangesAsync(cancellationToken);
            return loan.Id;
        });

    public Task<Result> UpdateAsync(Guid loanId, LoanInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var loan = await db.Set<Loan>().SingleOrDefaultAsync(d => d.Id == loanId, cancellationToken) ?? throw new NotFoundException();
            loan.SetBank(input.Bank, input.LoanNumber);
            Apply(loan, input);
            await db.SaveChangesAsync(cancellationToken);
            return loan.Id;
        });

    public Task<Result> AddExtraPaymentAsync(Guid loanId, ExtraPaymentInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await EnsureLoanAsync(loanId, cancellationToken);
            var extra = new LoanExtraPayment(
                loanId,
                input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."),
                input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."));
            db.Add(extra);
            await db.SaveChangesAsync(cancellationToken);
            return extra.Id;
        });

    public Task<Result> UpdateExtraPaymentAsync(Guid loanId, Guid extraPaymentId, ExtraPaymentInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var extra = await db.Set<LoanExtraPayment>().SingleOrDefaultAsync(s => s.Id == extraPaymentId && s.LoanId == loanId, cancellationToken) ?? throw new NotFoundException();
            extra.Change(input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."), input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."));
            await db.SaveChangesAsync(cancellationToken);
            return extraPaymentId;
        });

    public Task<Result> RemoveExtraPaymentAsync(Guid loanId, Guid extraPaymentId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var extra = await db.Set<LoanExtraPayment>().SingleOrDefaultAsync(s => s.Id == extraPaymentId && s.LoanId == loanId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(extra);
            await db.SaveChangesAsync(cancellationToken);
            return extraPaymentId;
        });

    public Task<Result> SaveCertificateAsync(Guid loanId, CertificateInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await EnsureLoanAsync(loanId, cancellationToken);
            var year = input.Year ?? throw new RuleViolationException("Bitte das Jahr angeben.");
            var interest = input.Interest ?? throw new RuleViolationException("Bitte die Jahreszinsen laut Bescheinigung angeben.");
            var certificate = await db.Set<LoanCertificate>().SingleOrDefaultAsync(b => b.LoanId == loanId && b.Year == year, cancellationToken);
            if (certificate is null)
            {
                certificate = new LoanCertificate(loanId, year, interest, input.ClosingBalance);
                db.Add(certificate);
            }
            else
            {
                certificate.Set(year, interest, input.ClosingBalance);
            }

            await db.SaveChangesAsync(cancellationToken);
            return certificate.Id;
        });

    public async Task<BankPlanPreview> PreviewBankPlanAsync(Guid loanId, byte[] csv, CancellationToken cancellationToken = default)
    {
        await EnsureLoanAsync(loanId, cancellationToken);
        return BankPlanCsv.Parse(TextDecoding.Read(csv));
    }

    public Task<Result> ImportBankPlanAsync(Guid loanId, byte[] csv, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            await EnsureLoanAsync(loanId, cancellationToken);
            var preview = BankPlanCsv.Parse(TextDecoding.Read(csv));
            if (!preview.CanImport)
            {
                throw new RuleViolationException(preview.Errors.Count > 0 ? preview.Errors[0] : "Die Datei enthält keine Raten.");
            }

            var existing = await db.Set<LoanCertificate>().Where(b => b.LoanId == loanId).ToDictionaryAsync(b => b.Year, cancellationToken);
            foreach (var year in preview.Years)
            {
                if (existing.TryGetValue(year.Year, out var certificate))
                {
                    certificate.Set(year.Year, year.Interest, year.ClosingBalance);
                }
                else
                {
                    db.Add(new LoanCertificate(loanId, year.Year, year.Interest, year.ClosingBalance));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return loanId;
        });

    public Task<Result> RemoveCertificateAsync(Guid loanId, Guid certificateId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var certificate = await db.Set<LoanCertificate>().SingleOrDefaultAsync(b => b.Id == certificateId && b.LoanId == loanId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(certificate);
            await db.SaveChangesAsync(cancellationToken);
            return certificateId;
        });

    public Task<Result> AddFinancingCostAsync(Guid loanId, FinancingCostInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var propertyId = await db.Set<Loan>().Where(d => d.Id == loanId).Select(d => (Guid?)d.PropertyId).SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException();
            var cost = new FinancingCost(
                propertyId,
                loanId,
                input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."),
                input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."),
                input.Kind,
                input.Description);
            db.Add(cost);
            await db.SaveChangesAsync(cancellationToken);
            return cost.Id;
        });

    public Task<Result> UpdateFinancingCostAsync(Guid loanId, Guid costId, FinancingCostInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var cost = await db.Set<FinancingCost>().SingleOrDefaultAsync(k => k.Id == costId && k.LoanId == loanId, cancellationToken) ?? throw new NotFoundException();
            cost.Change(
                input.Date ?? throw new RuleViolationException("Bitte das Datum angeben."),
                input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."),
                input.Kind,
                input.Description);
            await db.SaveChangesAsync(cancellationToken);
            return costId;
        });

    public Task<Result> RemoveFinancingCostAsync(Guid loanId, Guid costId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var cost = await db.Set<FinancingCost>().SingleOrDefaultAsync(k => k.Id == costId && k.LoanId == loanId, cancellationToken) ?? throw new NotFoundException();
            db.Remove(cost);
            await db.SaveChangesAsync(cancellationToken);
            return costId;
        });

    public async Task<FinancingExpenses> ExpensesAsync(Guid propertyId, int year, CancellationToken cancellationToken = default)
    {
        var loans = await db.Set<Loan>().AsNoTracking().Where(d => d.PropertyId == propertyId).ToListAsync(cancellationToken);
        var interest = 0m;
        foreach (var loan in loans)
        {
            var extras = await db.Set<LoanExtraPayment>().AsNoTracking().Where(s => s.LoanId == loan.Id).ToListAsync(cancellationToken);
            var certificates = await db.Set<LoanCertificate>().AsNoTracking().Where(b => b.LoanId == loan.Id).ToListAsync(cancellationToken);
            var plan = LoanCalculator.Calculate(loan.ToCalculation(), extras.Select(s => s.ToCalculation()).ToList());
            var row = YearsOf(plan, certificates).SingleOrDefault(j => j.Year == year);
            interest += Math.Round((row?.Interest ?? 0m) * loan.SharePercent / 100m, 2, MidpointRounding.AwayFromZero);
        }

        var oneOff = await db.Set<FinancingCost>().AsNoTracking()
            .Where(k => k.PropertyId == propertyId && k.Date.Year == year)
            .SumAsync(k => k.Amount, cancellationToken);
        return new FinancingExpenses(interest, oneOff);
    }

    /// <summary>Plan years merged with certificates: a certificate replaces the calculated interest and balance.</summary>
    private static List<LoanYearInterest> YearsOf(LoanPlan plan, IReadOnlyList<LoanCertificate> certificates)
    {
        var years = plan.Years.Select(j => j.Year).Union(certificates.Select(b => b.Year)).Order();
        return years.Select(year =>
        {
            var planYear = plan.Years.SingleOrDefault(j => j.Year == year);
            var certificate = certificates.SingleOrDefault(b => b.Year == year);
            return new LoanYearInterest(
                year,
                planYear?.Interest ?? 0m,
                certificate?.Interest,
                planYear?.Repayment ?? 0m,
                certificate?.ClosingBalance ?? planYear?.ClosingBalance ?? 0m);
        }).ToList();
    }

    private async Task EnsureLoanAsync(Guid loanId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Loan>().AnyAsync(d => d.Id == loanId, cancellationToken))
        {
            throw new NotFoundException();
        }
    }

    private static void Apply(Loan loan, LoanInput input)
    {
        var principal = input.Principal ?? throw new RuleViolationException("Bitte den Darlehensbetrag angeben.");
        var rate = input.InterestRatePercent ?? throw new RuleViolationException("Bitte den Sollzins angeben.");
        var instalment = input.Instalment
            ?? (input.InitialRepaymentPercent is { } repayment
                ? LoanCalculator.InstalmentFor(principal, rate, repayment, input.MonthsPerPeriod)
                : throw new RuleViolationException("Bitte die Rate oder die Anfangstilgung angeben."));
        loan.SetTerms(
            principal,
            input.DisbursementDate ?? throw new RuleViolationException("Bitte das Auszahlungsdatum angeben."),
            input.FirstPaymentDate ?? throw new RuleViolationException("Bitte die Fälligkeit der ersten Rate angeben."),
            rate,
            instalment,
            input.MonthsPerPeriod,
            input.FixedUntil,
            input.SharePercent ?? 100m,
            input.Notes);
    }

    private static LoanInput ToInput(Loan d) => new()
    {
        PropertyId = d.PropertyId,
        Bank = d.Bank,
        LoanNumber = d.LoanNumber,
        Principal = d.Principal,
        DisbursementDate = d.DisbursementDate,
        FirstPaymentDate = d.FirstPaymentDate,
        InterestRatePercent = d.InterestRatePercent,
        Instalment = d.Instalment,
        MonthsPerPeriod = d.MonthsPerPeriod,
        FixedUntil = d.FixedUntil,
        SharePercent = d.SharePercent,
        Notes = d.Notes,
    };
}
