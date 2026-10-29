using System.Globalization;
using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Communication;
using Elocaro.Application.Documents;
using Elocaro.Application.Finance;
using Elocaro.Application.People;
using Elocaro.Application.Todos;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Domain.Documents;
using Elocaro.Domain.Finance;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Leases;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Properties;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Elocaro.Infrastructure.Finance;

/// <summary>
/// Semi-automatic dunning. The job and the dashboard only <b>propose</b> (table <c>dunnings</c>, status <c>Proposed</c>);
/// the letter is created and sent exclusively in <see cref="IssueAsync"/> after approval.
/// </summary>
internal sealed class DunningService(
    ElocaroDbContext db,
    ITenantAccounts accounts,
    IPdfGenerator pdf,
    IMailSender mail,
    IDocumentLibrary documents,
    TimeProvider clock) : IDunning
{
    public async Task<int> RefreshAsync(Guid? leaseId = null, CancellationToken cancellationToken = default)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var rules = (await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken)).DunningRules;
        var open = leaseId is { } only ? await accounts.OpenItemsForLeaseAsync(only, cancellationToken) : await accounts.OpenItemsAsync(cancellationToken);
        var byLease = open.ToLookup(o => o.LeaseId);

        var proposalQuery = db.Set<Dunning>().Where(m => m.Status == DunningStatus.Proposed);
        if (leaseId is { } id)
        {
            proposalQuery = proposalQuery.Where(m => m.LeaseId == id);
        }

        var proposals = (await proposalQuery.ToListAsync(cancellationToken)).ToDictionary(m => m.LeaseId);
        var leaseIds = byLease.Select(g => g.Key).Union(proposals.Keys).ToList();
        var past = (await db.Set<Dunning>().AsNoTracking().Where(m => m.Status != DunningStatus.Proposed && leaseIds.Contains(m.LeaseId)).ToListAsync(cancellationToken)).ToLookup(m => m.LeaseId);
        var holds = (await db.Set<DunningHold>().AsNoTracking().Where(h => leaseIds.Contains(h.LeaseId)).ToListAsync(cancellationToken)).ToLookup(h => h.LeaseId);
        var leases = (await db.Set<Lease>().AsNoTracking().Include(v => v.Components).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken)).ToDictionary(v => v.Id);

        var changes = 0;
        foreach (var lease in leaseIds)
        {
            proposals.TryGetValue(lease, out var existing);
            var held = holds[lease].Any(h => h.IsActiveOn(today));
            var planned = held || !leases.ContainsKey(lease) ? null : PlanFor(rules, today, byLease[lease].ToList(), past[lease].ToList());
            if (planned is null)
            {
                if (existing is not null)
                {
                    db.Remove(existing);
                    changes++;
                }

                continue;
            }

            var (plan, interest) = planned.Value;
            var relevant = RelevanceOf(leases[lease], byLease[lease], today).Any;
            if (existing is null)
            {
                db.Add(new Dunning(lease, plan.Level, today, plan.Principal, plan.Fee, interest, plan.OldestDue, relevant));
                changes++;
            }
            else
            {
                changes += existing.Update(plan.Level, plan.Principal, plan.Fee, interest, plan.OldestDue, relevant) ? 1 : 0;
            }
        }

        if (changes == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception errors) when (DatabaseErrors.IsUniqueViolation(errors))
        {
            // Parallel run (job and dashboard) was faster; its result is equivalent.
            db.ChangeTracker.Clear();
            return 0;
        }

        return changes;
    }

    public async Task<DunningOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(null, cancellationToken);
        var today = GermanTime.Today(clock.GetUtcNow());
        var proposals = await db.Set<Dunning>().AsNoTracking().Where(m => m.Status == DunningStatus.Proposed).OrderBy(m => m.OldestDue).ToListAsync(cancellationToken);
        var issued = await db.Set<Dunning>().AsNoTracking().Where(m => m.Status == DunningStatus.Issued).OrderByDescending(m => m.DecidedOn).ThenByDescending(m => m.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var holds = await db.Set<DunningHold>().AsNoTracking().Where(h => h.Until == null || h.Until >= today).OrderBy(h => h.CreatedAt).ToListAsync(cancellationToken);
        var hints = await HintsAsync(await accounts.OpenItemsAsync(cancellationToken), cancellationToken);

        var leaseIds = proposals.Select(m => m.LeaseId).Concat(issued.Select(m => m.LeaseId)).Concat(holds.Select(h => h.LeaseId)).Distinct().ToList();
        var descriptions = await DescriptionsAsync(leaseIds, cancellationToken);
        return new DunningOverview(
            proposals.Select(m => Row(m, descriptions)).ToList(),
            issued.Select(m => Row(m, descriptions)).ToList(),
            holds.Select(h => HoldRow(h, descriptions)).ToList(),
            hints);
    }

    public async Task<LeaseDunning> ForLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        await RefreshAsync(leaseId, cancellationToken);
        var today = GermanTime.Today(clock.GetUtcNow());
        var all = await db.Set<Dunning>().AsNoTracking().Where(m => m.LeaseId == leaseId && m.Status != DunningStatus.Dismissed).ToListAsync(cancellationToken);
        var holds = await db.Set<DunningHold>().AsNoTracking().Where(h => h.LeaseId == leaseId && (h.Until == null || h.Until >= today)).OrderBy(h => h.CreatedAt).ToListAsync(cancellationToken);
        var descriptions = await DescriptionsAsync([leaseId], cancellationToken);
        var hint = (await HintsAsync(await accounts.OpenItemsForLeaseAsync(leaseId, cancellationToken), cancellationToken)).SingleOrDefault();
        return new LeaseDunning(
            all.Where(m => m.Status == DunningStatus.Proposed).Select(m => Row(m, descriptions)).FirstOrDefault(),
            all.Where(m => m.Status == DunningStatus.Issued).OrderByDescending(m => m.DecidedOn).Select(m => Row(m, descriptions)).ToList(),
            holds.Select(h => HoldRow(h, descriptions)).ToList(),
            hint);
    }

    public Task<DunningDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) => DetailsAsync(id, refresh: true, cancellationToken);

    public Task<DunningDetails?> PeekAsync(Guid id, CancellationToken cancellationToken = default) => DetailsAsync(id, refresh: false, cancellationToken);

    private async Task<DunningDetails?> DetailsAsync(Guid id, bool refresh, CancellationToken cancellationToken)
    {
        var proposal = await db.Set<Dunning>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (proposal is null)
        {
            return null;
        }

        if (refresh && proposal.Status == DunningStatus.Proposed)
        {
            // Opening revalidates: a payment since the proposal removes it.
            await RefreshAsync(proposal.LeaseId, cancellationToken);
            proposal = await db.Set<Dunning>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
            if (proposal is null)
            {
                return null;
            }
        }

        var prepared = await PrepareAsync(proposal, cancellationToken);
        var descriptions = await DescriptionsAsync([proposal.LeaseId], cancellationToken);
        var today = GermanTime.Today(clock.GetUtcNow());
        var hint = (await HintsAsync(await accounts.OpenItemsForLeaseAsync(proposal.LeaseId, cancellationToken), cancellationToken)).SingleOrDefault();
        var claims = prepared?.Plan.Claims.Select(c => new DunningClaimRow(c.Text, c.Due, c.Amount, c.Open)).ToList() ?? [];
        return new DunningDetails(
            Row(proposal, descriptions),
            claims,
            prepared?.Emails ?? [],
            prepared?.Address.Rows ?? [],
            today.AddDays(prepared?.Rules.DeadlineDays ?? DunningRules.Default.DeadlineDays),
            prepared?.Rules.InterestRate,
            hint,
            prepared?.Rules.ChargeInterest ?? false,
            prepared?.Landlord is null);
    }

    public async Task<byte[]> PreviewAsync(Guid id, DunningIssueInput options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var proposal = await db.Set<Dunning>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.Status == DunningStatus.Proposed, cancellationToken)
            ?? throw new InvalidOperationException("Der Vorschlag existiert nicht mehr.");
        var prepared = await PrepareAsync(proposal, cancellationToken);
        if (prepared is null || prepared.Plan.Level != proposal.Level)
        {
            throw new InvalidOperationException("Der Vorschlag ist nicht mehr aktuell.");
        }

        return await pdf.GenerateAsync(Request(prepared, options, GermanTime.Today(clock.GetUtcNow())), cancellationToken);
    }

    public Task<Result> IssueAsync(Guid id, DunningIssueInput options, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(options);
            var proposal = await db.Set<Dunning>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (proposal.Status != DunningStatus.Proposed)
            {
                throw new RuleViolationException("Diese Mahnung wurde bereits entschieden.");
            }

            var today = GermanTime.Today(clock.GetUtcNow());
            var prepared = await PrepareAsync(proposal, cancellationToken);
            if (prepared is null)
            {
                // Paid in the meantime (or held): the proposal disappears.
                db.Remove(proposal);
                await db.SaveChangesAsync(cancellationToken);
                throw new RuleViolationException("Der Rückstand besteht nicht mehr (Zahlung eingegangen oder Mahnsperre). Der Vorschlag wurde entfernt, es wurde nichts versendet.");
            }

            if (prepared.Plan.Level != proposal.Level)
            {
                proposal.Update(prepared.Plan.Level, prepared.Plan.Principal, prepared.Plan.Fee, prepared.Interest, prepared.Plan.OldestDue, proposal.TerminationRelevant);
                await db.SaveChangesAsync(cancellationToken);
                throw new RuleViolationException($"Der Vorschlag hat sich geändert (jetzt: {prepared.Plan.Level.Name()}). Bitte erneut prüfen. Es wurde nichts versendet.");
            }

            if (prepared.Landlord is null)
            {
                throw new RuleViolationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter). Es wird für Briefkopf und Absender benötigt.");
            }

            if (options.SendByMail && prepared.Emails.Count == 0)
            {
                throw new RuleViolationException("Für die Mieter ist keine E-Mail-Adresse hinterlegt. Bitte ohne E-Mail-Versand freigeben und das PDF per Post senden.");
            }

            var amounts = AmountsOf(prepared, options) with { Deadline = today.AddDays(prepared.Rules.DeadlineDays) };
            var request = Request(prepared, options, today);
            var title = prepared.Plan.Level.Name();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var file = await pdf.GenerateAndStoreAsync(request, $"{title} {today:yyyy-MM-dd}.pdf", cancellationToken);
            var documentResult = await documents.CreateAsync(file.Id, new DocumentInput
            {
                Title = $"{title} vom {today:dd.MM.yyyy}",
                Category = DocumentCategory.Correspondence,
                Date = today,
                LeaseId = proposal.LeaseId,
                Note = "Bei der Freigabe einer Mahnung automatisch abgelegt.",
            }, cancellationToken);
            if (!documentResult.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', documentResult.Errors));
            }

            Guid? feeCharge = null;
            Guid? interestCharge = null;
            var note = $"Aus der {title} vom {today:dd.MM.yyyy}.";
            if (amounts.Fee > 0)
            {
                var charge = new Charge(proposal.LeaseId, ChargeType.DunningFee, amounts.Deadline, amounts.Fee, $"Mahngebühr ({title})") { Note = note };
                db.Add(charge);
                feeCharge = charge.Id;
            }

            if (amounts.Interest > 0)
            {
                var charge = new Charge(proposal.LeaseId, ChargeType.DefaultInterest, amounts.Deadline, amounts.Interest, $"Verzugszinsen bis {today:dd.MM.yyyy}") { Note = note };
                db.Add(charge);
                interestCharge = charge.Id;
            }

            Guid? mailId = null;
            if (options.SendByMail)
            {
                mailId = await mail.EnqueueAsync(new MailRequest(
                    prepared.Emails,
                    "dunning",
                    new
                    {
                        Level = (int)prepared.Plan.Level,
                        LevelName = title,
                        prepared.Salutation,
                        LetterDate = today,
                        amounts.Deadline,
                        amounts.Total,
                        SenderName = prepared.Landlord.Name,
                    },
                    [file.Id],
                    $"dunning:{proposal.Id}"), cancellationToken);
            }

            proposal.Issue(today, amounts.Deadline, prepared.Plan.Principal, amounts.Fee, amounts.Interest, file.Id, documentResult.Id, mailId, feeCharge, interestCharge);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return proposal.Id;
        });

    public Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var proposal = await db.Set<Dunning>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException();
            proposal.Dismiss(GermanTime.Today(clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            return proposal.Id;
        });

    public Task<Result> AddHoldAsync(Guid leaseId, DunningHoldInput input, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!await db.Set<Lease>().AnyAsync(v => v.Id == leaseId, cancellationToken))
            {
                throw new NotFoundException();
            }

            var today = GermanTime.Today(clock.GetUtcNow());
            if (input.Until is { } until && until < today)
            {
                throw new RuleViolationException("Das Ende der Mahnsperre darf nicht in der Vergangenheit liegen.");
            }

            var hold = new DunningHold(leaseId, input.Reason.Trim(), input.Until);
            db.Add(hold);

            // A hold removes a proposal that is still open.
            foreach (var proposal in await db.Set<Dunning>().Where(m => m.LeaseId == leaseId && m.Status == DunningStatus.Proposed).ToListAsync(cancellationToken))
            {
                db.Remove(proposal);
            }

            await db.SaveChangesAsync(cancellationToken);
            return hold.Id;
        });

    public Task<Result> EndHoldAsync(Guid holdId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var hold = await db.Set<DunningHold>().SingleOrDefaultAsync(h => h.Id == holdId, cancellationToken) ?? throw new NotFoundException();
            hold.End(GermanTime.Today(clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            return hold.Id;
        });

    /// <summary>Plan including the interest net of what earlier dunnings already demanded.</summary>
    private static (DunningPlan Plan, decimal Interest)? PlanFor(DunningRules rules, DateOnly today, IReadOnlyList<OpenItem> open, IReadOnlyList<Dunning> past)
    {
        var claims = open.Select(o => new DunningClaim(o.ChargeId, o.Kind, o.Text, o.Due, o.Amount, o.Open)).ToList();
        var history = past.Where(m => m.DecidedOn is not null)
            .Select(m => new DunningHistoryEntry(m.Level, m.Status, m.DecidedOn!.Value)).ToList();
        var plan = DunningPlanner.Plan(rules, today, claims, history);
        if (plan is null)
        {
            return null;
        }

        var demandedBefore = past.Where(m => m.Status == DunningStatus.Issued && m.InterestChargeId is not null).Sum(m => m.Interest);
        return (plan, Math.Max(0m, plan.Interest - demandedBefore));
    }

    private static TerminationRelevance RelevanceOf(Lease lease, IEnumerable<OpenItem> open, DateOnly today) =>
        TerminationRelevance.Check(MonthlyRent(lease, today), open.Where(o => o.Kind == ChargeType.Rent).Select(o => (o.Due, o.Open)));

    private static decimal MonthlyRent(Lease lease, DateOnly today) =>
        lease.MonthlyRentOn(lease.End is { } end && end < today ? end : today);

    /// <summary>Tenancies whose arrears are relevant for termination (hint only), independent of any dunning proposal.</summary>
    private async Task<IReadOnlyList<TerminationHint>> HintsAsync(IReadOnlyList<OpenItem> open, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var leaseIds = open.Where(o => o.Kind == ChargeType.Rent).Select(o => o.LeaseId).Distinct().ToList();
        if (leaseIds.Count == 0)
        {
            return [];
        }

        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Components).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken);
        var descriptions = await DescriptionsAsync(leaseIds, cancellationToken);
        var hints = new List<TerminationHint>();
        foreach (var lease in leases)
        {
            var leaseOpen = open.Where(o => o.LeaseId == lease.Id).ToList();
            var relevance = RelevanceOf(lease, leaseOpen, today);
            if (relevance.Any)
            {
                var (rentalUnit, tenant) = descriptions[lease.Id];
                hints.Add(new TerminationHint(lease.Id, tenant, rentalUnit, MonthlyRent(lease, today), leaseOpen.Where(o => o.Kind == ChargeType.Rent).Sum(o => o.Open), relevance));
            }
        }

        return hints.OrderBy(h => h.Tenant, StringComparer.CurrentCulture).ToList();
    }

    private async Task<Dictionary<Guid, (string RentalUnit, string Tenant)>> DescriptionsAsync(IReadOnlyCollection<Guid> leaseIds, CancellationToken cancellationToken)
    {
        var leases = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Where(v => leaseIds.Contains(v.Id)).ToListAsync(cancellationToken);
        return await LeaseDescriptions.OfAsync(db, leases, cancellationToken);
    }

    private static DunningRow Row(Dunning m, IReadOnlyDictionary<Guid, (string RentalUnit, string Tenant)> descriptions)
    {
        var (rentalUnit, tenant) = descriptions.GetValueOrDefault(m.LeaseId, ("?", "?"));
        return new DunningRow(
            m.Id, m.LeaseId, tenant, rentalUnit, m.Level, m.Status, m.DecidedOn ?? m.ProposedOn, m.Principal, m.Fee, m.Interest,
            m.OldestDue, m.Deadline, m.FileId, m.SentByMail, m.TerminationRelevant);
    }

    private static DunningHoldRow HoldRow(DunningHold h, IReadOnlyDictionary<Guid, (string RentalUnit, string Tenant)> descriptions)
    {
        var (rentalUnit, tenant) = descriptions.GetValueOrDefault(h.LeaseId, ("?", "?"));
        return new DunningHoldRow(h.Id, h.LeaseId, tenant, rentalUnit, h.Reason, h.Until);
    }

    /// <summary>Everything needed for the letter; <c>null</c> if no dunning is due any more.</summary>
    private async Task<Prepared?> PrepareAsync(Dunning proposal, CancellationToken cancellationToken)
    {
        var today = GermanTime.Today(clock.GetUtcNow());
        var rules = (await FinanceSettingsService.GetOrCreateAsync(db, cancellationToken)).DunningRules;
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleOrDefaultAsync(v => v.Id == proposal.LeaseId, cancellationToken);
        var landlord = lease is null ? null : await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var open = await accounts.OpenItemsForLeaseAsync(lease.Id, cancellationToken);
        var past = await db.Set<Dunning>().AsNoTracking().Where(m => m.LeaseId == lease.Id && m.Status != DunningStatus.Proposed).ToListAsync(cancellationToken);
        var held = await db.Set<DunningHold>().AsNoTracking().Where(h => h.LeaseId == lease.Id).ToListAsync(cancellationToken);
        if (held.Any(h => h.IsActiveOn(today)) || PlanFor(rules, today, open, past) is not { } planned)
        {
            return null;
        }

        var (plan, interest) = planned;

        var personIds = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = (await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
            .OrderBy(p => personIds.IndexOf(p.Id)).ToList();
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var rentalUnit = rentalUnits.GetValueOrDefault(lease.RentalUnitId);
        var property = rentalUnit is null ? null : await db.Set<Property>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == rentalUnit.PropertyId, cancellationToken);
        var others = open.Where(o => Charge.RankOf(o.Kind) != AllocationRank.PrincipalClaim).OrderBy(o => o.Due).ToList();
        var demandedBefore = past.Where(m => m.Status == DunningStatus.Issued && m.InterestChargeId is not null).Sum(m => m.Interest);
        return new Prepared(
            rules, plan, interest, demandedBefore, others,
            people.Select(p => p.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            new PostalAddress(LetterParts.AddressRowsOf(people, rentalUnit?.Name, property, today)),
            LetterParts.SalutationOf(people),
            rentalUnit is null ? "" : $"{rentalUnit.Property}, {rentalUnit.Name}",
            landlord,
            past.Where(m => m.Status == DunningStatus.Issued).OrderByDescending(m => m.DecidedOn).FirstOrDefault());
    }

    private static Amounts AmountsOf(Prepared p, DunningIssueInput options)
    {
        var fee = options.IncludeFee ? p.Plan.Fee : 0m;
        var interest = options.IncludeInterest ? p.Interest : 0m;
        var other = p.Others.Sum(o => o.Open);
        return new Amounts(fee, interest, p.Plan.Principal + other + fee + interest, DateOnly.MinValue);
    }

    private static PdfRequest Request(Prepared p, DunningIssueInput options, DateOnly today)
    {
        var amounts = AmountsOf(p, options) with { Deadline = today.AddDays(p.Rules.DeadlineDays) };
        var level = p.Plan.Level;
        var landlord = p.Landlord ?? throw new InvalidOperationException("Es ist noch kein Vermieterprofil hinterlegt (Einstellungen → Vermieter).");
        var model = new
        {
            Level = (int)level,
            LevelName = level.Name(),
            IsReminder = level == DunningLevel.Reminder,
            IsFirstNotice = level == DunningLevel.FirstNotice,
            IsSecondNotice = level == DunningLevel.SecondNotice,
            p.Salutation,
            p.RentalUnit,
            Claims = p.Plan.Claims.Select(c => new { c.Text, c.Due, c.Amount, c.Open }).ToList(),
            OtherClaims = p.Others.Select(o => new { o.Text, o.Due, o.Amount, o.Open }).ToList(),
            Principal = p.Plan.Principal,
            OtherTotal = p.Others.Sum(o => o.Open),
            amounts.Fee,
            amounts.Interest,
            amounts.Total,
            amounts.Deadline,
            InterestRate = p.Rules.InterestRate,
            BaseRate = p.Rules.BaseRate,
            MarginPoints = p.Rules.InterestMarginPoints,
            InterestUntil = today,
            PreviousInterest = p.DemandedInterest,
            Previous = p.LastIssued is { } last ? (object)new { LevelName = last.Level.Name(), Date = last.DecidedOn } : null,
            SenderName = landlord.Name,
            landlord.Iban,
            landlord.Bic,
            landlord.Bank,
        };
        return new PdfRequest("dunning", level.Name(), model, p.Address, Date: today);
    }

    private sealed record Prepared(
        DunningRules Rules,
        DunningPlan Plan,
        decimal Interest,
        decimal DemandedInterest,
        IReadOnlyList<OpenItem> Others,
        IReadOnlyList<string> Emails,
        PostalAddress Address,
        string Salutation,
        string RentalUnit,
        LandlordProfile? Landlord,
        Dunning? LastIssued);

    private sealed record Amounts(decimal Fee, decimal Interest, decimal Total, DateOnly Deadline);
}

/// <summary>Daily: compares arrears with the proposals (sends nothing; sending is only possible through approval).</summary>
[DisallowConcurrentExecution]
internal sealed class DunningJob(IDunning dunning) : IJob
{
    public static readonly JobKey Key = new("dunning", "finance");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await dunning.RefreshAsync(null, cancellationToken);
    }
}

/// <summary>Dashboard: dunning proposals to review and arrears relevant for termination.</summary>
[Module(AppModule.Dunning)]
internal sealed class DunningTodos(IDunning dunning) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        var overview = await dunning.OverviewAsync(cancellationToken);
        var todos = new List<TodoItem>();
        if (overview.Proposals.Count > 0)
        {
            var count = overview.Proposals.Count;
            todos.Add(new TodoItem(
                "Finanzen",
                count == 1 ? "1 Mahnvorschlag prüfen" : $"{count} Mahnvorschläge prüfen",
                "/verwaltung/finanzen/mahnungen",
                overview.Proposals.Any(m => m.Level != DunningLevel.Reminder) ? Urgency.Urgent : Urgency.Soon,
                "Versendet wird erst nach Ihrer Freigabe.",
                overview.Proposals.Min(m => m.OldestDue)));
        }

        foreach (var hint in overview.TerminationHints)
        {
            todos.Add(new TodoItem(
                "Finanzen",
                $"Rückstand mit Kündigungsrelevanz: {hint.Tenant}",
                $"/verwaltung/vertraege/{hint.LeaseId}/konto",
                Urgency.Urgent,
                $"{hint.OpenRent.ToString("#,##0.00 €", CultureInfo.GetCultureInfo("de-DE"))} Mietrückstand (§ 543 Abs. 2 Nr. 3 BGB) – nur ein Hinweis, bitte rechtlich prüfen lassen."));
        }

        return todos;
    }
}

/// <summary>Dunnings and holds of a person's leases for the GDPR access request.</summary>
internal sealed class DunningPersonalData(ElocaroDbContext db) : IPersonalDataSource
{
    public string Section => "dunnings";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var leaseIds = await db.Set<LeaseParty>().Where(p => p.PersonId == personId && p.Role == PartyRole.Tenant).Select(p => p.LeaseId).ToListAsync(cancellationToken);
        if (leaseIds.Count == 0)
        {
            return null;
        }

        var dunnings = await db.Set<Dunning>().AsNoTracking().Where(m => leaseIds.Contains(m.LeaseId) && m.Status != DunningStatus.Proposed)
            .OrderBy(m => m.DecidedOn).Select(m => new { m.LeaseId, Level = m.Level.ToString(), Status = m.Status.ToString(), m.DecidedOn, m.Principal, m.Fee, m.Interest, m.SentByMail }).ToListAsync(cancellationToken);
        var holds = await db.Set<DunningHold>().AsNoTracking().Where(h => leaseIds.Contains(h.LeaseId))
            .Select(h => new { h.LeaseId, h.Reason, h.Until }).ToListAsync(cancellationToken);
        return dunnings.Count + holds.Count == 0 ? null : new { dunnings, holds };
    }
}
