using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Documents;
using MietHahn.Application.Leases;
using MietHahn.Application.Statements;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.OperatingCosts;
using MietHahn.Domain.People;
using MietHahn.Domain.Statements;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.OperatingCosts;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Properties;
using Zaehler = MietHahn.Domain.Metering.Meter;

namespace MietHahn.Infrastructure.Statements;

/// <summary>
/// Letter, booking and delivery of a released statement per tenancy. Rendering and sending follow the dunning: the letter is stored
/// immutably in the file store and shared in the document library (portal), the mail is the proof of dispatch.
/// </summary>
internal sealed class StatementDispatchService(
    MietHahnDbContext db,
    IStatements statements,
    IPdfGenerator pdf,
    IMailSender mail,
    IDocumentLibrary documents,
    ILeaseService leases,
    IOptions<StatementOptions> options,
    TimeProvider clock) : IStatementDispatch
{
    public async Task<DispatchOverview?> OverviewAsync(Guid statementId, CancellationToken cancellationToken = default)
    {
        var statement = await statements.GetAsync(statementId, cancellationToken);
        if (statement is null)
        {
            return null;
        }

        var today = GermanTime.Today(clock.GetUtcNow());
        var period = statement.Summary.BillingPeriod;
        var leaseIds = statement.Result.Tenants.Select(t => t.LeaseId).ToList();
        var leaseList = await db.Set<Lease>().AsNoTracking().IgnoreQueryFilters().Include(v => v.Parties).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken);
        var personIds = leaseList.SelectMany(v => v.Tenant).Select(p => p.PersonId).Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var dispatches = await db.Set<StatementDispatch>().AsNoTracking().Where(d => leaseIds.Contains(d.LeaseId)).ToListAsync(cancellationToken);
        var revisions = await db.Set<BillingStatement>().AsNoTracking()
            .Where(s => s.PropertyId == statement.Summary.PropertyId && s.BillingPeriod.From == period.From && s.BillingPeriod.To == period.To)
            .Select(s => new { s.Id, s.Revision }).ToListAsync(cancellationToken);
        var proposals = (await db.Set<PrepaymentProposal>().AsNoTracking().Where(p => leaseIds.Contains(p.LeaseId)).ToListAsync(cancellationToken)).ToLookup(p => p.DispatchId);
        var mails = await MailsAsync(dispatches, cancellationToken);
        var landlord = await db.Set<LandlordProfile>().AsNoTracking().AnyAsync(cancellationToken);
        var deadline = StatementDeadline.Of(period);

        var rows = new List<TenancyDispatch>();
        foreach (var tenant in statement.Result.Tenants.OrderBy(t => t.Rental, StringComparer.CurrentCulture).ThenBy(t => t.Term.From))
        {
            var lease = leaseList.Single(v => v.Id == tenant.LeaseId);
            var own = dispatches.Where(d => d.LeaseId == tenant.LeaseId).ToList();
            var earlier = own.Where(d => revisions.Any(r => r.Id == d.StatementId && r.Revision < statement.Summary.Revision)).Sum(d => d.BookedAmount);
            var (toBook, excluded) = Amounts(tenant.Balance, earlier, today > deadline);
            var current = own.SingleOrDefault(d => d.StatementId == statementId);
            rows.Add(new TenancyDispatch(
                tenant.LeaseId,
                tenant.Label,
                tenant.Rental,
                tenant.Balance,
                earlier,
                toBook,
                excluded,
                [.. lease.Tenant.Select(p => people.GetValueOrDefault(p.PersonId)?.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)],
                current is null ? null : ToRecord(current, mails, proposals[current.Id])));
        }

        return new DispatchOverview(statement, rows, deadline, !landlord);
    }

    public async Task<byte[]> PreviewAsync(Guid statementId, Guid leaseId, DispatchInput options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var prepared = await PrepareAsync(statementId, leaseId, options, cancellationToken);
        return await pdf.GenerateAsync(prepared.Request, cancellationToken);
    }

    public Task<Result> DispatchAsync(Guid statementId, Guid leaseId, DispatchInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var prepared = await PrepareAsync(statementId, leaseId, input, cancellationToken);
            if (!prepared.IsCurrent)
            {
                throw new RuleViolationException("Diese Fassung der Abrechnung wurde durch eine neuere ersetzt. Bitte die neue Fassung versenden.");
            }

            if (await db.Set<StatementDispatch>().AnyAsync(d => d.StatementId == statementId && d.LeaseId == leaseId, cancellationToken))
            {
                throw new RuleViolationException("Diese Abrechnung wurde für den Mietvertrag bereits versendet.");
            }

            if (input.Channel == DispatchChannel.Email && prepared.Emails.Count == 0)
            {
                throw new RuleViolationException("Für die Mieter ist keine E-Mail-Adresse hinterlegt. Bitte als Postversand erfassen und das PDF ausdrucken.");
            }

            var today = GermanTime.Today(clock.GetUtcNow());
            var period = prepared.Statement.Summary.BillingPeriod;
            var title = $"Betriebskostenabrechnung {Format(period)}";
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var file = await pdf.GenerateAndStoreAsync(prepared.Request, $"{title}{(prepared.Statement.Summary.Revision > 1 ? $" Fassung {prepared.Statement.Summary.Revision}" : "")} {prepared.Tenant.Label}.pdf", cancellationToken);
            var documentResult = await documents.CreateAsync(file.Id, new DocumentInput
            {
                Title = $"{title}{(prepared.Statement.Summary.Revision > 1 ? $" (Fassung {prepared.Statement.Summary.Revision})" : "")}",
                Category = DocumentCategory.Statement,
                Date = today,
                LeaseId = leaseId,
                PropertyId = prepared.Statement.Summary.PropertyId,
                SharedWithTenants = true,
                TenantsInformedElsewhere = true,
                Note = "Beim Versand der Abrechnung automatisch abgelegt und im Mieterportal bereitgestellt.",
            }, cancellationToken);
            if (!documentResult.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', documentResult.Errors));
            }

            Guid? chargeId = null;
            if (prepared.ToBook != 0)
            {
                var charge = new Charge(
                    leaseId,
                    prepared.ToBook > 0 ? ChargeType.AdditionalPayment : ChargeType.Refund,
                    prepared.Due,
                    prepared.ToBook,
                    $"{title}{(prepared.Statement.Summary.Revision > 1 ? $" (Fassung {prepared.Statement.Summary.Revision})" : "")}: {(prepared.ToBook > 0 ? "Nachzahlung" : "Guthaben")}")
                { Note = $"Aus der Abrechnung vom {today:dd.MM.yyyy}." };
                db.Add(charge);
                chargeId = charge.Id;
            }

            Guid? mailId = null;
            if (input.Channel == DispatchChannel.Email)
            {
                mailId = await mail.EnqueueAsync(new MailRequest(prepared.Emails, "statement", prepared.Model, [file.Id], $"statement:{statementId}:{leaseId}"), cancellationToken);
            }

            var dispatch = new StatementDispatch(
                statementId,
                leaseId,
                input.Channel,
                today,
                file.Id,
                documentResult.Id,
                mailId,
                prepared.Tenant.Balance,
                prepared.ToBook,
                chargeId,
                prepared.ClaimExcluded ? "Nachzahlung wegen abgelaufener Frist (§ 556 Abs. 3 BGB) nicht geltend gemacht." : null);
            db.Add(dispatch);
            foreach (var proposal in prepared.Proposals)
            {
                db.Add(new PrepaymentProposal(dispatch.Id, leaseId, proposal.Component, proposal.Current, proposal.NewAmount, proposal.EffectiveFrom));
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return dispatch.Id;
        });

    public Task<Result> ConfirmReceiptAsync(Guid dispatchId, DeliveryConfirmation input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var dispatch = await db.Set<StatementDispatch>().SingleOrDefaultAsync(d => d.Id == dispatchId, cancellationToken) ?? throw new NotFoundException();
            dispatch.ConfirmReceipt(input.ReceivedOn ?? throw new RuleViolationException("Bitte das Zugangsdatum angeben."), input.Note);
            await db.SaveChangesAsync(cancellationToken);
            return dispatch.Id;
        });

    public Task<Result> ApplyProposalAsync(Guid proposalId, decimal? amount, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var proposal = await db.Set<PrepaymentProposal>().SingleOrDefaultAsync(p => p.Id == proposalId, cancellationToken) ?? throw new NotFoundException();
            if (proposal.Status != ProposalStatus.Open)
            {
                throw new RuleViolationException("Der Vorschlag wurde bereits entschieden.");
            }

            var result = await leases.SetComponentAsync(proposal.LeaseId, new ComponentInput { Kind = proposal.Component, From = proposal.EffectiveFrom, Amount = amount ?? proposal.Proposed }, cancellationToken);
            if (!result.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', result.Errors));
            }

            proposal.Apply(GermanTime.Today(clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            return proposal.Id;
        });

    public Task<Result> DeclineProposalAsync(Guid proposalId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var proposal = await db.Set<PrepaymentProposal>().SingleOrDefaultAsync(p => p.Id == proposalId, cancellationToken) ?? throw new NotFoundException();
            proposal.Decline(GermanTime.Today(clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            return proposal.Id;
        });

    /// <summary>Amount to book and whether an additional payment is excluded because the deadline has passed.</summary>
    internal static (decimal ToBook, bool Excluded) Amounts(decimal balance, decimal earlierBooked, bool afterDeadline)
    {
        var raw = balance - earlierBooked;
        return raw > 0 && afterDeadline ? (0m, true) : (raw, false);
    }

    private static string Format(Period period) =>
        period.From.Month == 1 && period.From.Day == 1 && period.To is { Month: 12, Day: 31 } ? period.From.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{period.From:dd.MM.yyyy}–{period.To:dd.MM.yyyy}";

    private async Task<Dictionary<Guid, OutgoingMail>> MailsAsync(IReadOnlyList<StatementDispatch> dispatches, CancellationToken cancellationToken)
    {
        var ids = dispatches.Select(d => d.MailId).OfType<Guid>().ToList();
        return await db.Set<OutgoingMail>().AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
    }

    private static DispatchRecord ToRecord(StatementDispatch d, Dictionary<Guid, OutgoingMail> mails, IEnumerable<PrepaymentProposal> proposals)
    {
        var outgoing = d.MailId is { } id ? mails.GetValueOrDefault(id) : null;
        return new DispatchRecord(
            d.Id,
            d.Channel,
            d.SentOn,
            d.FileId,
            d.BookedAmount,
            d.Note,
            outgoing?.Status switch { MailStatus.Sent => "zugestellt", MailStatus.Failed => "fehlgeschlagen", MailStatus.Pending => "wartet auf Versand", _ => null },
            outgoing?.SentAt,
            d.ReceivedOn,
            d.ReceiptNote,
            [.. proposals.OrderBy(p => p.Component).Select(p => new ProposalRow(p.Id, p.Component, p.Current, p.Proposed, p.EffectiveFrom, p.Status, p.DecidedOn))]);
    }

    private async Task<Prepared> PrepareAsync(Guid statementId, Guid leaseId, DispatchInput input, CancellationToken cancellationToken)
    {
        var statement = await statements.GetAsync(statementId, cancellationToken) ?? throw new NotFoundException();
        var tenant = statement.Result.Tenants.FirstOrDefault(t => t.LeaseId == leaseId)
            ?? throw new RuleViolationException("Der Mietvertrag ist nicht Teil dieser Abrechnung.");
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Include(v => v.Components).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken) ?? throw new NotFoundException();
        var landlord = await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken)
            ?? throw new RuleViolationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter). Es wird für Briefkopf und Absender benötigt.");
        var today = GermanTime.Today(clock.GetUtcNow());
        var period = statement.Summary.BillingPeriod;

        var personIds = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = (await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
            .OrderBy(p => personIds.IndexOf(p.Id)).ToList();
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var rentalUnit = rentalUnits.GetValueOrDefault(lease.RentalUnitId);
        var property = rentalUnit is null ? null : await db.Set<Domain.Properties.Property>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == rentalUnit.PropertyId, cancellationToken);
        var emails = people.Select(p => p.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var earlierIds = await db.Set<BillingStatement>().AsNoTracking()
            .Where(s => s.PropertyId == statement.Summary.PropertyId && s.BillingPeriod.From == period.From && s.BillingPeriod.To == period.To && s.Revision < statement.Summary.Revision)
            .Select(s => s.Id).ToListAsync(cancellationToken);
        var earlier = await db.Set<StatementDispatch>().AsNoTracking().Where(d => d.LeaseId == leaseId && earlierIds.Contains(d.StatementId)).SumAsync(d => d.BookedAmount, cancellationToken);
        var deadline = StatementDeadline.Of(period);
        var (toBook, excluded) = Amounts(tenant.Balance, earlier, today > deadline);
        var due = today.AddDays(options.Value.PaymentDays);

        var safety = input.SafetyPercent ?? options.Value.PrepaymentSafetyPercent;
        var effective = input.PrepaymentFrom ?? PrepaymentCalculator.DefaultEffectiveDate(today);
        var proposals = new List<Proposed>();
        if (input.ProposePrepayments && (lease.Term.To is null || lease.Term.To > effective))
        {
            foreach (var (group, component, name) in new[]
            {
                (CostGroup.Operating, RentComponentType.OperatingCostPrepayment, "Betriebskostenvorauszahlung"),
                (CostGroup.Heating, RentComponentType.HeatingCostPrepayment, "Heizkostenvorauszahlung"),
            })
            {
                var balance = tenant.Groups.FirstOrDefault(g => g.Group == group && g.Calculated);
                if (balance is null || lease.AmountAt(component, today) is not { } current)
                {
                    continue;
                }

                var proposed = PrepaymentCalculator.Propose(balance.Share, tenant.Days, period.Days, safety);
                if (proposed != current)
                {
                    proposals.Add(new Proposed(component, name, current, proposed, effective));
                }
            }
        }

        var meters = await MeterRowsAsync(lease, period, cancellationToken);
        var operating = tenant.Groups.First(g => g.Group == CostGroup.Operating);
        var heating = tenant.Groups.FirstOrDefault(g => g.Group == CostGroup.Heating && g.Calculated
            && (g.PrepaymentsDue != 0 || g.PrepaymentsPaid != 0 || tenant.Lines.Any(l => l.Group == CostGroup.Heating)));
        var summary = statement.Summary;
        var model = new
        {
            Salutation = LetterParts.SalutationOf(people),
            RentalUnit = rentalUnit is null ? "" : $"{rentalUnit.Property}, {tenant.Rental}",
            PeriodFrom = period.From,
            PeriodTo = period.To!.Value,
            PeriodLabel = Format(period),
            TenancyFrom = tenant.Term.Intersect(period)?.From,
            TenancyTo = tenant.Term.Intersect(period)?.To,
            tenant.Days,
            Correction = summary.Revision > 1 ? new { summary.Revision, Reason = summary.Note ?? "" } : null,
            HasHeating = heating is not null,
            OperatingLines = Lines(tenant, CostGroup.Operating),
            HeatingLines = Lines(tenant, CostGroup.Heating),
            Operating = Group(operating),
            Heating = heating is null ? null : Group(heating),
            tenant.Balance,
            BalanceAbs = Math.Abs(tenant.Balance),
            EarlierBooked = earlier,
            ToBookRaw = tenant.Balance - earlier,
            ToBookAbs = Math.Abs(tenant.Balance - earlier),
            ToBook = toBook,
            ClaimWaived = excluded,
            Due = due,
            Proposals = proposals.Select(p => new { p.Name, p.Current, Proposed = p.NewAmount, p.EffectiveFrom }).ToList(),
            SafetyPercent = safety,
            Meters = meters,
            Sections = statement.Result.Sections.Select(s => new { s.Title, s.Notice, Entries = s.Entries.Select(e => new { e.Title, e.Formula, e.Values, e.Result, e.Note, e.Unit }).ToList() }).ToList(),
            Labor = new { Any = tenant.HouseholdLabor != 0 || tenant.TradesmanLabor != 0, Household = tenant.HouseholdLabor, Tradesman = tenant.TradesmanLabor },
            SenderName = landlord.Name,
            landlord.Iban,
            landlord.Bic,
            landlord.Bank,
        };
        var request = new PdfRequest("statement", $"Betriebskostenabrechnung {Format(period)}", model, new PostalAddress(new PostalAddress(LetterParts.AddressRowsOf(people, rentalUnit is null ? null : tenant.Rental, property, today)).Rows), Date: today, PropertyId: property?.Id);
        var isCurrent = summary.Current;
        return new Prepared(statement, tenant, request, model, emails, toBook, excluded, due, proposals, isCurrent);
    }

    private static List<object> Lines(TenantStatement tenant, CostGroup group) =>
        [.. tenant.Lines.Where(l => l.Group == group).Select(l => (object)new { l.Name, l.Key, l.CostTotal, l.Basis, l.Share, l.NotAgreed })];

    private static object Group(GroupBalance g) => new
    {
        g.Share,
        g.PrepaymentsPaid,
        g.PrepaymentsDue,
        g.Balance,
        BalanceAbs = Math.Abs(g.Balance),
    };

    /// <summary>Meters of the rented unit with readings at the start and end of the period.</summary>
    private async Task<List<object>> MeterRowsAsync(Lease lease, Period period, CancellationToken cancellationToken)
    {
        var unitId = lease.UnitId;
        var roomIds = lease.RoomIds;
        var propertyId = await db.Set<Domain.Properties.Unit>().AsNoTracking().Where(u => u.Id == unitId).Select(u => u.PropertyId).SingleAsync(cancellationToken);
        var all = await db.Set<Zaehler>().AsNoTracking().Include(m => m.Readings).Include(m => m.Terms).Where(m => m.PropertyId == propertyId).ToListAsync(cancellationToken);
        var source = new MeterConsumptionSource(all);
        var meters = all
            .Where(m => (m.UnitId == unitId && m.RoomId == null || roomIds.Contains(m.RoomId ?? Guid.Empty)) && m.InstalledOn <= period.To && (m.RemovedOn is null || m.RemovedOn >= period.From))
            .OrderBy(m => m.Type).ThenBy(m => m.Number).ToList();
        var rows = new List<object>();
        foreach (var meter in meters)
        {
            var start = meter.IsDerived ? null : ConsumptionCalculator.ReadingAt(meter.Readings, period.From.AddDays(-1));
            var end = meter.IsDerived ? null : ConsumptionCalculator.ReadingAt(meter.Readings, period.To!.Value);
            var consumption = meter.IsDerived
                ? source.MeterQuantity(meter.Id, period).Value
                : start is not null && end is not null ? end.Value - start.Value : (decimal?)null;
            rows.Add(new
            {
                meter.Number,
                Type = meter.IsDerived ? $"{TypeName(meter.Type)} (berechnet)" : TypeName(meter.Type),
                StartDate = period.From.AddDays(-1),
                StartValue = start?.Value,
                EndDate = period.To!.Value,
                EndValue = end?.Value,
                Derived = meter.IsDerived,
                Consumption = consumption,
                Unit = meter.MeasurementUnit switch { MeasurementUnit.CubicMeters => "m³", MeasurementUnit.KilowattHours => "kWh", MeasurementUnit.MegawattHours => "MWh", _ => "l" },
            });
        }

        return rows;
    }

    private static string TypeName(MeterType type) => type switch
    {
        MeterType.ColdWater => "Kaltwasser",
        MeterType.HotWater => "Warmwasser",
        MeterType.Heat => "Wärme",
        MeterType.Electricity => "Strom",
        MeterType.Gas => "Gas",
        _ => "Öltank",
    };

    private sealed record Proposed(RentComponentType Component, string Name, decimal Current, decimal NewAmount, DateOnly EffectiveFrom);

    private sealed record Prepared(
        StoredStatement Statement,
        TenantStatement Tenant,
        PdfRequest Request,
        object Model,
        IReadOnlyList<string> Emails,
        decimal ToBook,
        bool ClaimExcluded,
        DateOnly Due,
        IReadOnlyList<Proposed> Proposals,
        bool IsCurrent);
}
