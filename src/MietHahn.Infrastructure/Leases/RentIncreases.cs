using System.Net;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Leases;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Documents;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Properties;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Leases;

internal sealed class RentIncreases(MietHahnDbContext db, IPdfGenerator pdf, IDocumentLibrary documents, TimeProvider clock) : IRentIncreases
{
    public async Task<RentIncreaseOverview?> OverviewAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var context = await LoadAsync(leaseId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var lease = context.Lease;
        var today = Today;
        var baseRent = lease.AmountAt(RentComponentType.BaseRent, today);
        var lastIncrease = lease.LastBaseRentChange(today);
        var effective = ComparativeRent.EffectiveFrom(today.AddDays(3));
        var rows = await db.Set<RentIncrease>().AsNoTracking().Where(r => r.LeaseId == leaseId).OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
        return new RentIncreaseOverview(
            leaseId, context.RentalUnit.Name, context.RentalUnit.Property, lease.RentModel, lease.BillingType, baseRent, lastIncrease,
            RentThreeYearsBefore(lease, effective), context.Property.RentCapPercent, context.Area,
            ComparativeRent.EarliestDeclaration(lastIncrease ?? lease.Start, lastIncrease is not null),
            lease.AmountAt(RentComponentType.OperatingCostFlatRate, today),
            await IndexProposalAsync(lease, baseRent, today, cancellationToken),
            lease.RentSteps.Where(s => s.AppliedAt is null && s.From > today).OrderBy(s => s.From).Select(s => new RentStepView(s.Id, s.From, s.BaseRent, s.AppliedAt)).FirstOrDefault(),
            rows.Select(r => new RentIncreaseRow(r.Id, r.Kind, r.Status, r.CurrentAmount, r.NewAmount, r.PlannedDeclaration, r.EffectiveFrom, r.DeclaredOn, r.ConsentDeadline, r.ConsentOn, r.DocumentId, r.Summary)).ToList());
    }

    public async Task<RentIncreaseCheck> CheckAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var context = await LoadAsync(leaseId, cancellationToken);
        if (context is null)
        {
            return Failed("Der Mietvertrag wurde nicht gefunden.");
        }

        var plan = await PlanAsync(context, input, cancellationToken);
        return new RentIncreaseCheck(plan.ComparativeRent, plan.NewAmount, plan.Errors, plan.Warnings, plan.Assessment?.EarliestDeclaration, plan.EffectiveFrom, plan.ConsentDeadline, plan.Assessment?.CapLimit, plan.Assessment?.MaxRent);
    }

    public async Task<TemplatePreview> PreviewAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            var context = await LoadAsync(leaseId, cancellationToken) ?? throw new RuleViolationException("Der Mietvertrag wurde nicht gefunden.");
            var plan = await PlanAsync(context, input, cancellationToken);
            if (plan.Errors.Count > 0)
            {
                return new TemplatePreview(null, plan.Errors);
            }

            var request = await RequestAsync(context, plan, input.TemplateId, cancellationToken);
            return new TemplatePreview(await pdf.GenerateAsync(request.Request, cancellationToken), []);
        }
        catch (Exception ex) when (ex is RuleViolationException or InvalidOperationException)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (HttpRequestException)
        {
            return new TemplatePreview(null, ["Der PDF-Dienst ist nicht erreichbar."]);
        }
    }

    public async Task<Result> CreateDraftAsync(Guid leaseId, RentIncreaseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var context = await LoadAsync(leaseId, cancellationToken);
        if (context is null)
        {
            return Command.NotFound;
        }

        var plan = await PlanAsync(context, input, cancellationToken);
        if (plan.Errors.Count > 0)
        {
            return Result.Failed([.. plan.Errors]);
        }

        return await Command.ExecuteAsync(async () =>
        {
            var letter = await RequestAsync(context, plan, input.TemplateId, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            Domain.Files.StoredFile file;
            try
            {
                file = await pdf.GenerateAndStoreAsync(letter.Request, FileName(letter.Request.Title, context), cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new RuleViolationException("Der PDF-Dienst ist nicht erreichbar; es wurde nichts verändert.");
            }

            var document = await documents.CreateAsync(file.Id, new DocumentInput
            {
                Title = $"{letter.Request.Title} – {context.TenantNames}".Truncate(200),
                Category = DocumentCategory.Correspondence,
                Date = Today,
                LeaseId = context.Lease.Id,
                TemplateRevisionId = letter.RevisionId,
                Note = $"Entwurf zur Mieterhöhung, noch nicht zugegangen. Vorlage „{letter.TemplateName}“.",
            }, cancellationToken);
            if (!document.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', document.Errors));
            }

            var increase = new RentIncrease(context.Lease.Id, input.Kind, plan.Current, plan.NewAmount!.Value, plan.Planned, plan.EffectiveFrom!.Value, plan.ConsentDeadline, plan.Summary)
            {
                TemplateRevisionId = letter.RevisionId,
                DocumentId = document.Id,
            };
            plan.Fill?.Invoke(increase);
            db.Add(increase);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return increase.Id;
        });
    }

    public Task<Result> DeclareAsync(Guid id, DateOnly received, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var increase = await db.Set<RentIncrease>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            var context = await LoadAsync(increase.LeaseId, cancellationToken) ?? throw new NotFoundException();
            var lease = context.Lease;
            if (increase.Status != RentIncreaseStatus.Draft)
            {
                increase.Declare(received, increase.EffectiveFrom, increase.ConsentDeadline); // throws the matching message
            }

            switch (increase.Kind)
            {
                case RentIncreaseKind.Comparative:
                    var assessment = ComparativeRent.Assess(lease.Start, lease.LastBaseRentChange(received), increase.CurrentAmount, RentThreeYearsBefore(lease, ComparativeRent.EffectiveFrom(received)),
                        increase.CapPercent ?? context.Property.RentCapPercent, increase.ComparativeRentAmount, received, increase.NewAmount);
                    if (!assessment.IsAllowed)
                    {
                        throw new RuleViolationException("Mit diesem Zugangsdatum ist das Verlangen nicht zulässig: " + string.Join(' ', assessment.Errors));
                    }

                    increase.Declare(received, assessment.EffectiveFrom, assessment.ConsentDeadline);
                    break;

                case RentIncreaseKind.Index:
                    var effective = IndexedRent.EffectiveFrom(received);
                    var earliest = IndexedRent.EarliestEffectiveDate(lease.LastBaseRentChange() ?? lease.Start);
                    if (effective < earliest)
                    {
                        throw new RuleViolationException($"Die Miete muss mindestens ein Jahr unverändert bleiben: die Anpassung kann frühestens ab {earliest:dd.MM.yyyy} wirken.");
                    }

                    var newRent = lease.AdjustIndex(effective, increase.IndexNewMonth!.Value, increase.IndexNewValue!.Value);
                    increase.Restate(newRent);
                    increase.Declare(received, effective, null);
                    break;

                case RentIncreaseKind.Modernisation:
                    var modernisationFrom = ModernisationLevy.EffectiveFrom(received, increase.EffectiveExtended);
                    var delta = increase.NewAmount - increase.CurrentAmount;
                    lease.SetComponent(RentComponentType.BaseRent, modernisationFrom, (lease.AmountAt(RentComponentType.BaseRent, modernisationFrom) ?? increase.CurrentAmount) + delta);
                    increase.Declare(received, modernisationFrom, null);
                    break;

                case RentIncreaseKind.FlatRate:
                    var flatFrom = IndexedRent.EffectiveFrom(received);
                    lease.SetComponent(RentComponentType.OperatingCostFlatRate, flatFrom, increase.NewAmount);
                    increase.Declare(received, flatFrom, null);
                    break;

                default:
                    increase.Declare(received, increase.EffectiveFrom, null);
                    break;
            }

            await db.SaveChangesAsync(cancellationToken);
            return increase.Id;
        });

    public Task<Result> AgreeAsync(Guid id, DateOnly consentedOn, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var increase = await db.Set<RentIncrease>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            var context = await LoadAsync(increase.LeaseId, cancellationToken) ?? throw new NotFoundException();
            increase.Agree(consentedOn);
            context.Lease.SetComponent(RentComponentType.BaseRent, increase.EffectiveFrom, increase.NewAmount);
            await db.SaveChangesAsync(cancellationToken);
            return increase.Id;
        });

    public Task<Result> RefuseAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var increase = await db.Set<RentIncrease>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            increase.Refuse();
            await db.SaveChangesAsync(cancellationToken);
            return increase.Id;
        });

    public Task<Result> WithdrawAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var increase = await db.Set<RentIncrease>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException();
            increase.Withdraw();
            await db.SaveChangesAsync(cancellationToken);
            return increase.Id;
        });

    private static RentIncreaseCheck Failed(string error) => new(null, null, [error], [], null, null, null, null, null);

    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    /// <summary>Base rent three years before the day the increase takes effect (at the start of the lease if it is younger).</summary>
    private static decimal RentThreeYearsBefore(Lease lease, DateOnly effectiveFrom)
    {
        var day = effectiveFrom.AddYears(-ComparativeRent.CapYears);
        return lease.AmountAt(RentComponentType.BaseRent, day < lease.Start ? lease.Start : day) ?? 0m;
    }

    private async Task<IndexProposal?> IndexProposalAsync(Lease lease, decimal? baseRent, DateOnly today, CancellationToken cancellationToken)
    {
        if (lease.RentModel != RentModel.Index || lease.IndexBaseValue is not { } baseValue || lease.IndexBaseMonth is not { } baseMonth || baseRent is not { } rent)
        {
            return null;
        }

        var newest = await db.Set<CpiValue>().AsNoTracking().Where(v => v.Month > baseMonth).OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
        if (newest is null)
        {
            return null;
        }

        var earliest = IndexedRent.EarliestEffectiveDate(lease.LastBaseRentChange() ?? lease.Start);
        return new IndexProposal(newest.Month, newest.Value, IndexedRent.ChangePercent(baseValue, newest.Value), IndexedRent.NewRent(rent, baseValue, newest.Value), earliest, IndexedRent.EffectiveFrom(today) >= earliest);
    }

    private async Task<Plan> PlanAsync(Context context, RentIncreaseInput input, CancellationToken cancellationToken)
    {
        var planned = input.PlannedDeclaration ?? Today.AddDays(3);
        var errors = new List<string>();
        var warnings = new List<string>();
        var lease = context.Lease;
        var current = lease.AmountAt(RentComponentType.BaseRent, planned < lease.Start ? lease.Start : planned) ?? 0m;
        var baseModel = BaseModel(context, input.Kind, planned);

        switch (input.Kind)
        {
            case RentIncreaseKind.Comparative:
                return PlanComparative(context, input, planned, current, baseModel, errors, warnings);
            case RentIncreaseKind.Index:
                return await PlanIndexAsync(context, planned, current, baseModel, errors, warnings, cancellationToken);
            case RentIncreaseKind.Graduated:
                return PlanGraduated(context, planned, baseModel, errors, warnings);
            case RentIncreaseKind.Modernisation:
                return await PlanModernisationAsync(context, input, planned, current, baseModel, errors, warnings, cancellationToken);
            default:
                return PlanFlatRate(context, input, planned, baseModel, errors, warnings);
        }
    }

    private static Plan PlanComparative(Context context, RentIncreaseInput input, DateOnly planned, decimal current, RentIncreaseModel baseModel, List<string> errors, List<string> warnings)
    {
        var lease = context.Lease;
        if (lease.RentModel != RentModel.Standard)
        {
            errors.Add(lease.RentModel == RentModel.Graduated
                ? "Bei einer Staffelmiete ist eine Erhöhung auf die Vergleichsmiete ausgeschlossen (§ 557a Abs. 2 BGB)."
                : "Bei einer Indexmiete ist eine Erhöhung auf die Vergleichsmiete ausgeschlossen (§ 557b Abs. 2 BGB).");
        }

        var area = context.Area;
        decimal? comparative = null;
        string basisText;
        var model = baseModel with { IsComparative = true };
        switch (input.Basis)
        {
            case ComparativeBasis.RentIndex:
                if (area is not > 0)
                {
                    errors.Add("Für den Mietspiegel fehlt die Wohnfläche in den Stammdaten.");
                }

                if (input.PerSquareMetre is not > 0)
                {
                    errors.Add("Bitte den Mietspiegelwert in € pro m² angeben.");
                }
                else if ((input.PerSquareMetreFrom is { } from && input.PerSquareMetre < from) || (input.PerSquareMetreTo is { } to && input.PerSquareMetre > to))
                {
                    errors.Add("Der Wert liegt außerhalb der Spanne des Mietspiegels.");
                }

                if (string.IsNullOrWhiteSpace(input.RentIndexName))
                {
                    errors.Add("Bitte den Mietspiegel benennen (Gemeinde, Jahr).");
                }

                if (area is > 0 && input.PerSquareMetre is > 0)
                {
                    comparative = ComparativeRent.FromPerSquareMetre(input.PerSquareMetre.Value, area.Value);
                }

                basisText = $"Mietspiegel {input.RentIndexName?.Trim()}, {input.PerSquareMetre:0.00} €/m²";
                model = model with
                {
                    BasisRentIndex = true,
                    RentIndexName = input.RentIndexName?.Trim(),
                    PerSquareMetreFrom = input.PerSquareMetreFrom,
                    PerSquareMetreTo = input.PerSquareMetreTo,
                    PerSquareMetre = input.PerSquareMetre,
                    LivingArea = area,
                    FeatureText = input.FeatureText?.Trim(),
                };
                if (string.IsNullOrWhiteSpace(input.FeatureText))
                {
                    warnings.Add("Die Einordnung in den Mietspiegel sollte mit den Merkmalen der Wohnung begründet werden (Baujahr, Ausstattung, Lage).");
                }

                break;

            case ComparativeBasis.Comparables:
                var flats = input.Comparables.Where(c => c.Rent is > 0 && c.Area is > 0 && !string.IsNullOrWhiteSpace(c.Address)).ToList();
                if (flats.Count < 3)
                {
                    errors.Add("Für Vergleichswohnungen bitte mindestens drei Wohnungen mit Anschrift, Fläche und Miete angeben (§ 558a Abs. 2 Nr. 4 BGB).");
                }

                if (area is not > 0)
                {
                    errors.Add("Für den Vergleich fehlt die Wohnfläche in den Stammdaten.");
                }

                var listed = flats.Select(c => new ComparableFlat(c.Address.Trim(), c.Area!.Value, c.Rent!.Value, Math.Round(c.Rent.Value / c.Area.Value, 2, MidpointRounding.AwayFromZero))).ToList();
                if (listed.Count >= 3 && area is > 0)
                {
                    // All comparable flats must support the demanded rent, so the lowest price per m² counts.
                    comparative = ComparativeRent.FromPerSquareMetre(listed.Min(c => c.PerSquareMetre), area.Value);
                }

                basisText = $"{listed.Count} Vergleichswohnungen";
                model = model with { BasisComparables = true, Comparables = listed, LivingArea = area };
                break;

            default:
                if (input.AppraisalRent is not > 0)
                {
                    errors.Add("Bitte die im Gutachten ermittelte Vergleichsmiete angeben.");
                }

                if (string.IsNullOrWhiteSpace(input.AppraiserName))
                {
                    errors.Add("Bitte den Sachverständigen angeben.");
                }

                comparative = input.AppraisalRent;
                basisText = $"Gutachten {input.AppraiserName?.Trim()}";
                model = model with { BasisAppraisal = true, AppraiserName = input.AppraiserName?.Trim(), AppraisalDate = input.AppraisalDate };
                break;
        }

        var cap = context.Property.RentCapPercent;
        var effective = ComparativeRent.EffectiveFrom(planned);
        var rentThreeYearsAgo = RentThreeYearsBefore(lease, effective);
        var lastIncrease = lease.LastBaseRentChange(planned);
        var capLimit = ComparativeRent.CapLimit(rentThreeYearsAgo, cap);
        var provisionalMax = comparative is { } c0 ? Math.Min(c0, capLimit) : capLimit;
        var newRent = input.NewAmount ?? provisionalMax;
        var assessment = ComparativeRent.Assess(lease.Start, lastIncrease, current, rentThreeYearsAgo, cap, comparative, planned, newRent);
        errors.AddRange(assessment.Errors);
        warnings.AddRange(assessment.Warnings);

        model = model with
        {
            CurrentAmount = current,
            NewAmount = newRent,
            IncreaseAmount = newRent - current,
            IncreasePercent = current > 0 ? Math.Round((newRent - current) / current * 100, 1, MidpointRounding.AwayFromZero) : 0m,
            EffectiveFrom = assessment.EffectiveFrom,
            ConsentDeadline = assessment.ConsentDeadline,
            LastIncrease = lastIncrease,
            ComparativeRent = comparative,
            CapPercent = cap,
            CapLimit = capLimit,
            RentThreeYearsAgo = rentThreeYearsAgo,
        };

        return new Plan(errors, warnings, current, newRent, planned, assessment.EffectiveFrom, assessment.ConsentDeadline, basisText, model, assessment, comparative,
            r =>
            {
                r.Basis = input.Basis;
                r.ComparativeRentAmount = comparative;
                r.CapPercent = cap;
                r.CapLimit = capLimit;
            });
    }

    private async Task<Plan> PlanIndexAsync(Context context, DateOnly planned, decimal current, RentIncreaseModel baseModel, List<string> errors, List<string> warnings, CancellationToken cancellationToken)
    {
        var lease = context.Lease;
        DateOnly effective = IndexedRent.EffectiveFrom(planned);
        decimal? newRent = null;
        CpiValue? newest = null;
        if (lease.RentModel != RentModel.Index || lease.IndexBaseValue is not { } baseValue || lease.IndexBaseMonth is not { } baseMonth)
        {
            errors.Add("Der Vertrag ist keine Indexmiete (mit Ausgangsindex).");
        }
        else
        {
            newest = await db.Set<CpiValue>().AsNoTracking().Where(v => v.Month > baseMonth).OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
            if (newest is null)
            {
                errors.Add("Es gibt keinen VPI-Wert, der neuer ist als der Ausgangsindex. Bitte zuerst den Verbraucherpreisindex pflegen.");
            }
            else
            {
                newRent = IndexedRent.NewRent(current, baseValue, newest.Value);
                if (newRent <= current)
                {
                    errors.Add("Der Index ist nicht gestiegen; eine Erhöhung ist nicht möglich (eine Senkung wäre auf Verlangen des Mieters zu beachten).");
                }

                var earliest = IndexedRent.EarliestEffectiveDate(lease.LastBaseRentChange() ?? lease.Start);
                if (effective < earliest)
                {
                    errors.Add($"Die Miete muss mindestens ein Jahr unverändert bleiben: die Anpassung kann frühestens ab {earliest:dd.MM.yyyy} wirken.");
                }

                baseModel = baseModel with
                {
                    OldIndex = baseValue,
                    NewIndex = newest.Value,
                    OldIndexMonth = baseMonth,
                    NewIndexMonth = newest.Month,
                    IndexChangePercent = IndexedRent.ChangePercent(baseValue, newest.Value),
                };
            }
        }

        var model = baseModel with
        {
            IsIndex = true,
            CurrentAmount = current,
            NewAmount = newRent ?? current,
            IncreaseAmount = (newRent ?? current) - current,
            IncreasePercent = current > 0 ? Math.Round(((newRent ?? current) - current) / current * 100, 1, MidpointRounding.AwayFromZero) : 0m,
            EffectiveFrom = effective,
            LastIncrease = lease.LastBaseRentChange(),
        };
        return new Plan(errors, warnings, current, newRent, planned, effective, null, newest is null ? "Indexanpassung" : $"VPI {model.OldIndex:0.0} → {newest.Value:0.0}", model, null, null,
            r =>
            {
                r.IndexOldMonth = model.OldIndexMonth;
                r.IndexOldValue = model.OldIndex;
                r.IndexNewMonth = newest?.Month;
                r.IndexNewValue = newest?.Value;
            });
    }

    private async Task<Plan> PlanModernisationAsync(Context context, RentIncreaseInput input, DateOnly planned, decimal current, RentIncreaseModel baseModel, List<string> errors, List<string> warnings, CancellationToken cancellationToken)
    {
        var lease = context.Lease;
        if (lease.RentModel == RentModel.Graduated)
        {
            warnings.Add("Bei einer Staffelmiete ist eine Mieterhöhung wegen Modernisierung nur eingeschränkt möglich (§ 557a Abs. 2 BGB): bitte prüfen.");
        }

        if (string.IsNullOrWhiteSpace(input.ModernisationDescription))
        {
            errors.Add("Die Erklärung muss die Modernisierungsmaßnahme bezeichnen und die Mieterhöhung berechnen und erläutern (§ 559b Abs. 1 BGB): bitte die Maßnahme beschreiben.");
        }

        var effective = ModernisationLevy.EffectiveFrom(planned, !input.AnnouncedProperly);
        var since = effective.AddYears(-ModernisationLevy.CapYears);
        var earlier = (await db.Set<RentIncrease>().AsNoTracking()
            .Where(r => r.LeaseId == lease.Id && r.Kind == RentIncreaseKind.Modernisation && r.Status == RentIncreaseStatus.InForce && r.EffectiveFrom > since)
            .Select(r => new { r.NewAmount, r.CurrentAmount }).ToListAsync(cancellationToken)).Sum(r => r.NewAmount - r.CurrentAmount);
        var costs = input.ModernisationCosts ?? 0m;
        var assessment = ModernisationLevy.Assess(current, context.Area, costs, input.MaintenanceShare ?? 0m, input.Simplified, earlier);
        errors.AddRange(assessment.Errors);
        warnings.AddRange(assessment.Warnings);
        if (!input.AnnouncedProperly)
        {
            warnings.Add("Ohne ordnungsgemäße Ankündigung (§ 555c BGB) oder bei über 10 % höheren Kosten beginnt die Erhöhung sechs Monate später (§ 559b Abs. 2 BGB).");
        }

        warnings.Add("Die Mieterhöhung setzt eine Modernisierung nach § 555b Nr. 1, 3, 4, 5 oder 6 BGB voraus (Energieeinsparung, Wasser, Gebrauchswert, allgemeine Wohnverhältnisse, bauliche Änderungen auf Grund Umständen, die der Vermieter nicht zu vertreten hat). Reine Instandhaltung ist nicht umlagefähig.");
        var maintenance = input.Simplified ? Math.Round(costs * ModernisationLevy.SimplifiedMaintenanceShare, 2, MidpointRounding.AwayFromZero) : input.MaintenanceShare ?? 0m;
        var newRent = current + assessment.AllowedIncrease;
        var model = baseModel with
        {
            IsModernisation = true,
            CurrentAmount = current,
            NewAmount = newRent,
            IncreaseAmount = assessment.AllowedIncrease,
            IncreasePercent = current > 0 ? Math.Round(assessment.AllowedIncrease / current * 100, 1, MidpointRounding.AwayFromZero) : 0m,
            EffectiveFrom = effective,
            LastIncrease = lease.LastBaseRentChange(planned),
            LivingArea = context.Area,
            ModernisationDescription = input.ModernisationDescription?.Trim(),
            ModernisationCosts = costs,
            MaintenanceShare = maintenance,
            ChargeableCosts = assessment.ChargeableCosts,
            CapPerSquareMetre = assessment.CapPerSquareMetre,
            EarlierIncreases = earlier,
            IsSimplified = input.Simplified,
            IsExtended = !input.AnnouncedProperly,
        };
        return new Plan(errors, warnings, current, assessment.IsAllowed ? newRent : null, planned, effective, null, $"Modernisierung, {assessment.ChargeableCosts:N0} € umlagefähig".Replace(",", ".", StringComparison.Ordinal), model, null, null,
            r =>
            {
                r.ModernisationCosts = costs;
                r.MaintenanceShare = maintenance;
                r.EffectiveExtended = !input.AnnouncedProperly;
            });
    }

    private static Plan PlanGraduated(Context context, DateOnly planned, RentIncreaseModel baseModel, List<string> errors, List<string> warnings)
    {
        var lease = context.Lease;
        var upcoming = lease.RentSteps.Where(s => s.AppliedAt is null && s.From > planned).OrderBy(s => s.From).ToList();
        if (lease.RentModel != RentModel.Graduated || upcoming.Count == 0)
        {
            errors.Add("Es gibt keine kommende Staffel, über die informiert werden könnte.");
            return new Plan(errors, warnings, 0m, null, planned, planned, null, "Staffelmiete", baseModel with { IsGraduated = true }, null, null, null);
        }

        var step = upcoming[0];
        var current = lease.AmountAt(RentComponentType.BaseRent, step.From.AddDays(-1)) ?? 0m;
        warnings.Add("Die Staffel gilt ohne weitere Erklärung zum vereinbarten Termin; das Schreiben ist eine freiwillige Information.");
        var model = baseModel with
        {
            IsGraduated = true,
            CurrentAmount = current,
            NewAmount = step.BaseRent,
            IncreaseAmount = step.BaseRent - current,
            IncreasePercent = current > 0 ? Math.Round((step.BaseRent - current) / current * 100, 1, MidpointRounding.AwayFromZero) : 0m,
            EffectiveFrom = step.From,
            NextSteps = upcoming.Select(s => new ContractStep(s.From, s.BaseRent)).ToList(),
        };
        return new Plan(errors, warnings, current, step.BaseRent, planned, step.From, null, $"Staffel ab {step.From:dd.MM.yyyy}", model, null, null, null);
    }

    private static Plan PlanFlatRate(Context context, RentIncreaseInput input, DateOnly planned, RentIncreaseModel baseModel, List<string> errors, List<string> warnings)
    {
        var lease = context.Lease;
        var current = lease.AmountAt(RentComponentType.OperatingCostFlatRate, planned < lease.Start ? lease.Start : planned) ?? 0m;
        if (lease.BillingType != BillingType.FlatRate || current <= 0)
        {
            errors.Add("Der Vertrag hat keine Betriebskostenpauschale.");
        }

        if (!input.ReservationConfirmed)
        {
            errors.Add("Eine Pauschale lässt sich nur erhöhen, wenn der Mietvertrag das vorsieht (§ 560 Abs. 1 BGB). Bitte den Vorbehalt bestätigen.");
        }

        if (string.IsNullOrWhiteSpace(input.CostReason))
        {
            errors.Add("Die Erklärung ist nur wirksam, wenn der Grund der Erhöhung bezeichnet und erläutert wird (§ 560 Abs. 4 BGB): bitte die gestiegenen Kosten erläutern.");
        }

        if (input.NewAmount is not > 0)
        {
            errors.Add("Bitte die neue Pauschale angeben.");
        }
        else if (input.NewAmount <= current)
        {
            errors.Add("Die neue Pauschale muss höher sein als die bisherige.");
        }

        warnings.Add("Die Heizkostenverordnung hat Vorrang: eine Pauschale, die Heizung und Warmwasser abdeckt, ist bei zentraler Heizung grundsätzlich nicht zulässig.");
        var effective = IndexedRent.EffectiveFrom(planned);
        var newAmount = input.NewAmount ?? current;
        var model = baseModel with
        {
            IsFlatRate = true,
            CurrentAmount = current,
            NewAmount = newAmount,
            IncreaseAmount = newAmount - current,
            IncreasePercent = current > 0 ? Math.Round((newAmount - current) / current * 100, 1, MidpointRounding.AwayFromZero) : 0m,
            EffectiveFrom = effective,
            CostReason = input.CostReason?.Trim(),
        };
        return new Plan(errors, warnings, current, input.NewAmount, planned, effective, null, "Anpassung der Betriebskostenpauschale", model, null, null, null);
    }

    private static RentIncreaseModel BaseModel(Context context, RentIncreaseKind kind, DateOnly planned)
    {
        var landlord = context.Landlord;
        return new RentIncreaseModel(
            planned, context.Salutation, context.TenantNames, $"{context.RentalUnit.Name}, {context.RentalUnit.Property}", context.Property.Name, context.Property.Street, context.Property.PostalCode, context.Property.City,
            landlord.Name, $"{landlord.Street}, {landlord.PostalCode} {landlord.City}", landlord.Iban is null ? null : Iban.Format(landlord.Iban), landlord.Bank,
            kind == RentIncreaseKind.Comparative, kind == RentIncreaseKind.Index, kind == RentIncreaseKind.Graduated, kind == RentIncreaseKind.FlatRate,
            0m, 0m, 0m, 0m, planned, null, null, context.Lease.Start,
            false, false, false, null, null, null, null, null, null, [], null, null, null, 0, 0m, 0m,
            null, null, null, null, null, [], null);
    }

    private async Task<(PdfRequest Request, Guid RevisionId, string TemplateName)> RequestAsync(Context context, Plan plan, Guid? templateId, CancellationToken cancellationToken)
    {
        var template = await db.Set<DocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new RuleViolationException("Die Vorlage wurde nicht gefunden.");
        if (template.Kind != TemplateKind.RentIncrease)
        {
            throw new RuleViolationException("Bitte eine Vorlage der Art „Mieterhöhung“ wählen.");
        }

        var revision = await db.Set<DocumentTemplateRevision>().AsNoTracking().SingleAsync(r => r.TemplateId == template.Id && r.Number == template.Revision, cancellationToken);
        string title;
        try
        {
            title = Decode(ScribanRenderer.RenderText(template.Subject, plan.Model, snippets: await TemplateSnippets.LoadAsync(db, cancellationToken)).Html);
        }
        catch (TemplateErrorException ex)
        {
            throw new RuleViolationException(ex.Message);
        }

        var request = new PdfRequest(null, title, plan.Model, new PostalAddress(LetterParts.AddressRowsOf(context.Tenants, context.RentalUnit.Name, context.Property, Today)), template.Body, Today, context.Property.Id);
        return (request, revision.Id, template.Name);
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html).Trim();

    private static string FileName(string title, Context context)
    {
        var cleaned = string.Concat($"{title} {context.TenantNames}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return cleaned.Truncate(200) + ".pdf";
    }

    private async Task<Context?> LoadAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().Include(v => v.Parties).Include(v => v.Components).Include(v => v.RentSteps).Include(v => v.People).AsSplitQuery()
            .SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var rentalUnit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId);
        if (rentalUnit is null)
        {
            return null;
        }

        var property = await db.Set<Property>().AsNoTracking().SingleAsync(o => o.Id == rentalUnit.PropertyId, cancellationToken);
        var unit = await db.Set<Unit>().AsNoTracking().Include(e => e.Areas).Include(e => e.Room).ThenInclude(z => z.Areas).AsSplitQuery().SingleAsync(e => e.Id == lease.UnitId, cancellationToken);
        var room = lease.RoomId is { } roomId ? unit.Room.SingleOrDefault(z => z.Id == roomId) : null;
        var today = Today;
        decimal? area = room is not null ? room.AreaOn(today)?.AllocationArea : unit.AreaOn(today)?.LivingArea;
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = await db.Set<Person>().AsNoTracking().Include(p => p.Addresses).Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var tenants = ids.Select(i => people.First(p => p.Id == i)).ToList();
        var landlord = await SenderProfiles.ForPropertyAsync(db, property.Id, cancellationToken);
        return new Context(lease, rentalUnit, property, area, tenants, landlord ?? new LandlordProfile("(Vermieter fehlt)", "", "", ""),
            string.Join(" und ", tenants.Select(p => p.DisplayName)), LetterParts.SalutationOf(tenants));
    }

    private sealed record Context(Lease Lease, RentalUnit RentalUnit, Property Property, decimal? Area, List<Person> Tenants, LandlordProfile Landlord, string TenantNames, string Salutation);

    private sealed record Plan(
        List<string> Errors,
        List<string> Warnings,
        decimal Current,
        decimal? NewAmount,
        DateOnly Planned,
        DateOnly? EffectiveFrom,
        DateOnly? ConsentDeadline,
        string Summary,
        RentIncreaseModel Model,
        ComparativeRentAssessment? Assessment,
        decimal? ComparativeRent,
        Action<RentIncrease>? Fill);
}
