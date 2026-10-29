using Elocaro.Application.Admin;
using Elocaro.Application.Common;
using Elocaro.Application.Leases;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Leases;

internal sealed class LeaseService(ElocaroDbContext db, TimeProvider clock, IModuleState modules) : ILeaseService
{
    private const string Overlap = "Für dieses Mietobjekt gibt es in diesem Zeitraum bereits einen Vertrag.";

    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<LeaseOverview>> ListAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var query = WithDetails();
        if (activeOnly)
        {
            query = query.Where(v => v.Term.To == null || v.Term.To >= today);
        }

        var leases = await query.ToListAsync(cancellationToken);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var names = await NamesAsync(leases.SelectMany(v => v.Parties.Select(p => p.PersonId)), cancellationToken);
        return leases
            .Select(v => new LeaseOverview(
                v.Id,
                RentalUnit.LabelOf(v.RentalUnitId, v.ExtraRooms.Select(r => r.RoomId), rentalUnits),
                rentalUnits.GetValueOrDefault(v.RentalUnitId)?.Property ?? "–",
                TenantText(v, names),
                v.Start,
                v.End,
                v.StatusAt(today),
                v.MonthlyRentOn(v.Start > today ? v.Start : today),
                v.BillingType))
            .OrderBy(v => v.Property, StringComparer.CurrentCulture)
            .ThenBy(v => v.RentalUnit, StringComparer.CurrentCulture)
            .ThenByDescending(v => v.Start)
            .ToList();
    }

    public async Task<LeaseDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lease = await WithDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (lease is null)
        {
            return null;
        }

        var today = Today;
        var referenceDate = today < lease.Start ? lease.Start : lease.End is { } end && today > end ? end : today;
        var rentalUnit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId);
        var people = await db.Set<Person>().AsNoTracking().Where(p => lease.Parties.Select(x => x.PersonId).Contains(p.Id)).ToListAsync(cancellationToken);
        var rooms = lease.RoomId is null ? [] : await db.Set<Room>().AsNoTracking().Where(z => z.UnitId == lease.UnitId).ToListAsync(cancellationToken);

        return new LeaseDetails(
            lease.Id,
            lease.RentalUnitId,
            lease.UnitId,
            lease.RoomId,
            rentalUnit?.PropertyId ?? Guid.Empty,
            RentalUnit.LabelOf(lease.RentalUnitId, lease.ExtraRooms.Select(r => r.RoomId), await RentalUnit.GetAsync(db, cancellationToken)),
            rentalUnit?.Property ?? "–",
            lease.Term,
            lease.FixedTermUntil,
            lease.StatusAt(today),
            lease.TerminationReceivedOn,
            lease.TerminatedBy,
            Data(lease),
            lease.Parties.OrderBy(p => p.Role).ThenBy(p => p.Order).Select(p =>
            {
                var person = people.Single(x => x.Id == p.PersonId);
                return new LeasePartyView(p.PersonId, person.DisplayName, p.Role, person.Email, person.Phone ?? person.Mobile);
            }).ToList(),
            lease.ComponentsAt(referenceDate).Select(Entry).ToList(),
            lease.Components.OrderBy(b => b.Kind).ThenByDescending(b => b.Validity.From).Select(Entry).ToList(),
            lease.People.OrderByDescending(p => p.Validity.From).Select(p => new OccupantCountView(p.Id, p.Validity, p.Count)).ToList(),
            lease.MonthlyRentOn(referenceDate),
            referenceDate,
            NextDueDate(lease, today),
            lease.LandlordNoticePeriodMonths(today),
            await RentModelAsync(lease, referenceDate, today, cancellationToken),
            rooms.Where(z => lease.RoomIds.Contains(z.Id)).OrderBy(z => lease.RoomIds.ToList().IndexOf(z.Id)).Select(z => new LeaseRoomView(z.Id, z.Name, z.Id == lease.RoomId)).ToList(),
            [.. rooms.Where(z => !lease.RoomIds.Contains(z.Id)).OrderBy(z => z.Name, StringComparer.Ordinal).Select(z => new LeaseRoomView(z.Id, z.Name, false))]);
    }

    public async Task<IReadOnlyList<RentalUnitOption>> RentalUnitsAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        var ongoing = await WithDetails().Where(v => v.Term.From <= today && (v.Term.To == null || v.Term.To >= today)).ToListAsync(cancellationToken);
        var names = await NamesAsync(ongoing.SelectMany(v => v.Parties.Select(p => p.PersonId)), cancellationToken);
        return rentalUnits.Values.Where(m => m.IsRentable).Select(m =>
        {
            var lease = ongoing.FirstOrDefault(v => v.RentalUnitId == m.Id || v.ExtraRooms.Any(r => r.RoomId == m.Id));
            return new RentalUnitOption(m.Id, m.Name, m.Property, m.RoomId is not null, lease is null ? null : new Occupied(TenantText(lease, names), lease.End));
        }).ToList();
    }

    public async Task<IReadOnlyList<LeaseHistoryEntry>> HistoryAsync(Guid rentalUnitId, CancellationToken cancellationToken = default)
    {
        var leases = await WithDetails().Where(v => v.RentalUnitId == rentalUnitId).ToListAsync(cancellationToken);
        var names = await NamesAsync(leases.SelectMany(v => v.Parties.Select(p => p.PersonId)), cancellationToken);
        var entries = new List<LeaseHistoryEntry>();
        Lease? previous = null;
        foreach (var lease in leases.OrderBy(v => v.Start))
        {
            // Gap between two leases = vacancy
            if (previous?.End is { } end && lease.Start > end.AddDays(1))
            {
                entries.Add(new(null, new Period(end.AddDays(1), lease.Start.AddDays(-1)), null, null));
            }

            entries.Add(new(lease.Id, lease.Term, TenantText(lease, names), lease.AmountAt(RentComponentType.BaseRent, lease.Start)));
            previous = lease;
        }

        if (previous?.End is { } lastEnd && lastEnd < Today)
        {
            entries.Add(new(null, new Period(lastEnd.AddDays(1)), null, null));
        }

        entries.Reverse();
        return entries;
    }

    public async Task<Result> CreateAsync(LeaseCreation creation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creation);
        var rentalUnits = await RentalUnit.GetAsync(db, cancellationToken);
        if (creation.RentalUnitId is not { } rentalUnitId || !rentalUnits.TryGetValue(rentalUnitId, out var rentalUnit))
        {
            return Result.Failed("Bitte das Mietobjekt wählen.");
        }

        if (!rentalUnit.IsRentable)
        {
            return Result.Failed($"{rentalUnit.Name} ist eine WG und wird nur zimmerweise vermietet.");
        }

        var parties = creation.TenantIds.Distinct().Select(p => (p, PartyRole.Tenant))
            .Concat(creation.GuarantorIds.Distinct().Except(creation.TenantIds).Select(p => (p, PartyRole.Guarantor))).ToList();
        if (creation.TenantIds.Count == 0)
        {
            return Result.Failed("Bitte mindestens einen Mieter wählen.");
        }

        var ids = parties.Select(p => p.p).ToList();
        if (await db.Set<Person>().CountAsync(p => ids.Contains(p.Id) && p.AnonymizedAt == null, cancellationToken) != ids.Count)
        {
            return Result.Failed("Mindestens eine gewählte Person wurde nicht gefunden.");
        }

        return await Command.ExecuteAsync(async () =>
        {
            var start = creation.Start ?? throw new RuleViolationException("Bitte den Mietbeginn angeben.");
            var lease = new Lease(rentalUnit.Id, rentalUnit.UnitId, rentalUnit.RoomId, start, creation.FixedTermUntil);
            if (rentalUnit.RoomId is { } firstRoom)
            {
                await EnsureRoomFreeAsync(firstRoom, lease.Term, null, cancellationToken);
            }

            foreach (var roomId in creation.AdditionalRoomIds.Distinct().Where(r => r != rentalUnit.RoomId))
            {
                await EnsureRoomOfUnitAsync(roomId, rentalUnit.UnitId, cancellationToken);
                await EnsureRoomFreeAsync(roomId, lease.Term, null, cancellationToken);
                lease.AddRoom(roomId);
            }

            Apply(lease, creation.Data);
            lease.BillingType = creation.BillingType;
            lease.HeatingCostsConsumptionBased = creation.HeatingCostsConsumptionBased;
            foreach (var (personId, role) in parties)
            {
                lease.AddParty(personId, role);
            }

            lease.SetComponent(RentComponentType.BaseRent, start, creation.BaseRent ?? throw new RuleViolationException("Bitte die Kaltmiete angeben."));
            Optional(lease, RentComponentType.OperatingCostPrepayment, start, creation.OperatingCostPrepayment);
            Optional(lease, RentComponentType.HeatingCostPrepayment, start, creation.HeatingCostPrepayment);
            Optional(lease, RentComponentType.OperatingCostFlatRate, start, creation.OperatingCostFlatRate);
            lease.SetOccupantCount(start, creation.People);
            if (creation.RentModel != RentModel.Standard)
            {
                lease.SetRentModel(creation.RentModel, creation.IndexBaseMonth, creation.IndexBaseValue);
            }

            ValidateBillingType(lease, start);

            db.Add(lease);
            await db.SaveChangesAsync(cancellationToken);
            await modules.RefreshUsageAsync(cancellationToken);
            return lease.Id;
        }, Overlap);
    }

    public Task<Result> UpdateAsync(Guid id, LeaseInput data, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(data);
            Apply(lease, data);
            lease.BillingType = data.BillingType;
            lease.HeatingCostsConsumptionBased = data.HeatingCostsConsumptionBased;
            ValidateBillingType(lease, lease.Components.Where(c => c.Kind == RentComponentType.OperatingCostFlatRate).Select(c => c.Validity.From).DefaultIfEmpty(lease.Start).Max());
        }, cancellationToken);

    public Task<Result> AddRoomAsync(Guid id, Guid roomId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var lease = await WithDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException();
            await EnsureRoomOfUnitAsync(roomId, lease.UnitId, cancellationToken);
            await EnsureRoomFreeAsync(roomId, lease.Term, lease.Id, cancellationToken);
            lease.AddRoom(roomId);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> RemoveRoomAsync(Guid id, Guid roomId, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RemoveRoom(roomId), cancellationToken);

    private async Task EnsureRoomOfUnitAsync(Guid roomId, Guid unitId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Room>().AnyAsync(z => z.Id == roomId && z.UnitId == unitId, cancellationToken))
        {
            throw new RuleViolationException("Das Zimmer gehört nicht zur Wohngemeinschaft des Vertrags.");
        }
    }

    /// <summary>The room must not be rented in the term by another lease (as its first or a further room).</summary>
    private async Task EnsureRoomFreeAsync(Guid roomId, Period term, Guid? exceptLeaseId, CancellationToken cancellationToken)
    {
        var to = term.To;
        var busy = await db.Set<Lease>().AsNoTracking()
            .Where(v => v.Id != exceptLeaseId && (v.RoomId == roomId || v.ExtraRooms.Any(r => r.RoomId == roomId)))
            .Where(v => (to == null || v.Term.From <= to) && (v.Term.To == null || v.Term.To >= term.From))
            .FirstOrDefaultAsync(cancellationToken);
        if (busy is not null)
        {
            throw new RuleViolationException($"Das Zimmer ist im Zeitraum schon vermietet (ab {busy.Start:dd.MM.yyyy}).");
        }
    }

    public Task<Result> CorrectTermAsync(Guid id, LeaseTermInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.CorrectTerm(input.Start ?? throw new RuleViolationException("Bitte den Mietbeginn angeben."), input.FixedTermUntil);
        }, cancellationToken);

    public Task<Result> SetComponentAsync(Guid id, ComponentInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.SetComponent(
                input.Kind,
                input.From ?? throw new RuleViolationException("Bitte angeben, ab wann der Betrag gilt."),
                input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."),
                input.Name);
        }, cancellationToken);

    public Task<Result> CorrectComponentAsync(Guid id, Guid componentId, ComponentCorrection input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.CorrectComponent(
                componentId,
                input.From ?? throw new RuleViolationException("Bitte den Beginn angeben."),
                input.To,
                input.Amount ?? throw new RuleViolationException("Bitte den Betrag angeben."),
                input.Name);
        }, cancellationToken);

    public Task<Result> RemoveComponentAsync(Guid id, Guid componentId, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RemoveComponent(componentId), cancellationToken);

    public Task<Result> CorrectOccupantCountAsync(Guid id, Guid entryId, OccupantCountCorrection input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.CorrectOccupantCount(entryId, input.From ?? throw new RuleViolationException("Bitte den Beginn angeben."), input.To, input.Count);
        }, cancellationToken);

    public Task<Result> RemoveOccupantCountAsync(Guid id, Guid entryId, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RemoveOccupantCount(entryId), cancellationToken);

    public Task<Result> EndComponentAsync(Guid id, RentComponentType kind, DateOnly lastDay, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.EndComponent(kind, lastDay), cancellationToken);

    public Task<Result> SetOccupantCountAsync(Guid id, OccupantCountInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.SetOccupantCount(input.From ?? throw new RuleViolationException("Bitte angeben, ab wann die Anzahl gilt."), input.Count);
        }, cancellationToken);

    public async Task<Result> AddPartyAsync(Guid id, Guid personId, PartyRole role, CancellationToken cancellationToken = default)
    {
        if (!await db.Set<Person>().AnyAsync(p => p.Id == personId && p.AnonymizedAt == null, cancellationToken))
        {
            return Result.Failed("Die Person wurde nicht gefunden.");
        }

        return await WithLeaseAsync(id, lease => lease.AddParty(personId, role), cancellationToken);
    }

    public Task<Result> RemovePartyAsync(Guid id, Guid personId, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RemoveParty(personId), cancellationToken);

    public Task<Result> TerminateAsync(Guid id, TerminationInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var incoming = input.ReceivedOn ?? throw new RuleViolationException("Bitte das Eingangsdatum der Kündigung angeben.");
            var effectiveOn = input.EffectiveDate ?? input.By switch
            {
                TerminatingParty.Tenant => Lease.TerminationDateForTenant(incoming, lease.NoticePeriodMonths),
                TerminatingParty.Landlord => Lease.TerminationDateForTenant(incoming, lease.LandlordNoticePeriodMonths(incoming)),
                _ => throw new RuleViolationException("Bei einer einvernehmlichen Beendigung bitte das Vertragsende angeben."),
            };
            if (lease.FixedTermUntil is { } fixedTerm && effectiveOn > fixedTerm)
            {
                effectiveOn = fixedTerm;
            }

            lease.Terminate(incoming, input.By, effectiveOn);
        }, cancellationToken);

    public Task<Result> RevokeTerminationAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RevokeTermination(), cancellationToken);

    public async Task<Result> MoveOutAsync(Guid id, MoveOutInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var withAddress = !string.IsNullOrWhiteSpace(input.NewStreet);
        if (withAddress && (string.IsNullOrWhiteSpace(input.NewPostalCode) || string.IsNullOrWhiteSpace(input.NewCity)))
        {
            return Result.Failed("Bitte zur neuen Straße auch PLZ und Ort angeben.");
        }

        return await Command.ExecuteAsync(async () =>
        {
            var lease = await WithDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException();
            var end = input.End ?? throw new RuleViolationException("Bitte den letzten Tag des Mietverhältnisses angeben.");
            if (lease.TerminationReceivedOn is { } incoming && lease.TerminatedBy is { } by)
            {
                lease.Terminate(incoming <= end ? incoming : end, by, end);
            }
            else
            {
                lease.Terminate(end, TerminatingParty.Mutual, end);
            }

            if (withAddress)
            {
                var tenantIds = lease.Tenant.Select(m => m.PersonId).ToList();
                var tenant = await db.Set<Person>().Include(p => p.Addresses).Where(p => tenantIds.Contains(p.Id)).ToListAsync(cancellationToken);
                foreach (var person in tenant)
                {
                    person.SetAddress(end.AddDays(1), input.NewStreet!, input.NewPostalCode!, input.NewCity!);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return lease.Id;
        }, Overlap);
    }

    public Task<Result> SetRentModelAsync(Guid id, RentModelInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.SetRentModel(input.Model, input.IndexBaseMonth, input.IndexBaseValue);
        }, cancellationToken);

    public Task<Result> AddRentStepAsync(Guid id, RentStepInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.AddRentStep(
                input.From ?? throw new RuleViolationException("Bitte den Termin der Staffel angeben."),
                input.BaseRent ?? throw new RuleViolationException("Bitte die neue Kaltmiete angeben."));
        }, cancellationToken);

    public Task<Result> UpdateRentStepAsync(Guid id, Guid rentStepId, RentStepInput input, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease =>
        {
            ArgumentNullException.ThrowIfNull(input);
            lease.UpdateRentStep(
                rentStepId,
                input.From ?? throw new RuleViolationException("Bitte den Termin der Staffel angeben."),
                input.BaseRent ?? throw new RuleViolationException("Bitte die neue Kaltmiete angeben."));
        }, cancellationToken);

    public Task<Result> RemoveRentStepAsync(Guid id, Guid rentStepId, CancellationToken cancellationToken = default) =>
        WithLeaseAsync(id, lease => lease.RemoveRentStep(rentStepId), cancellationToken);

    public async Task<Result> AdjustIndexAsync(Guid id, IndexAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var newest = await db.Set<CpiValue>().AsNoTracking().OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
        return await WithLeaseAsync(id, lease =>
        {
            var declaration = input.DeclaredOn ?? throw new RuleViolationException("Bitte angeben, wann die Erklärung zugeht.");
            if (newest is null || lease.IndexBaseMonth is null || newest.Month <= lease.IndexBaseMonth)
            {
                throw new RuleViolationException("Es gibt keinen VPI-Wert, der neuer ist als der Ausgangsindex. Bitte zuerst den VPI pflegen.");
            }

            lease.AdjustIndex(IndexedRent.EffectiveFrom(declaration), newest.Month, newest.Value);
        }, cancellationToken);
    }

    private async Task<RentModelView> RentModelAsync(Lease lease, DateOnly referenceDate, DateOnly today, CancellationToken cancellationToken)
    {
        var baseRent = lease.AmountAt(RentComponentType.BaseRent, referenceDate);
        var threeYearsAgo = referenceDate.AddYears(-3);
        IndexProposal? suggestion = null;
        if (lease.RentModel == RentModel.Index && lease.IndexBaseValue is { } baseValue && lease.IndexBaseMonth is { } baseMonth && baseRent is { } rent)
        {
            var newest = await db.Set<CpiValue>().AsNoTracking().Where(v => v.Month > baseMonth).OrderByDescending(v => v.Month).FirstOrDefaultAsync(cancellationToken);
            if (newest is not null)
            {
                var earliest = IndexedRent.EarliestEffectiveDate(lease.LastBaseRentChange() ?? lease.Start);
                suggestion = new IndexProposal(
                    newest.Month,
                    newest.Value,
                    IndexedRent.ChangePercent(baseValue, newest.Value),
                    IndexedRent.NewRent(rent, baseValue, newest.Value),
                    earliest,
                    IndexedRent.EffectiveFrom(today) >= earliest);
            }
        }

        return new RentModelView(
            lease.RentModel,
            lease.LastBaseRentChange(referenceDate),
            threeYearsAgo >= lease.Start ? lease.AmountAt(RentComponentType.BaseRent, threeYearsAgo) : null,
            baseRent,
            lease.IndexBaseMonth,
            lease.IndexBaseValue,
            lease.RentSteps.OrderBy(s => s.From).Select(s => new RentStepView(s.Id, s.From, s.BaseRent, s.AppliedAt)).ToList(),
            suggestion);
    }

    private static void ValidateBillingType(Lease lease, DateOnly day)
    {
        if (lease.BillingType == BillingType.FlatRate && lease.AmountAt(RentComponentType.OperatingCostFlatRate, day) is null)
        {
            throw new RuleViolationException("Bei der Abrechnungsart „Pauschale“ bitte die Betriebskostenpauschale angeben.");
        }

        if (lease.BillingType == BillingType.Inclusive && lease.AmountAt(RentComponentType.OperatingCostFlatRate, day) is null)
        {
            throw new RuleViolationException("Bei der Abrechnungsart „Inklusivmiete“ bitte den enthaltenen Nebenkostenanteil angeben.");
        }
    }

    private static void Optional(Lease lease, RentComponentType kind, DateOnly from, decimal? amount)
    {
        if (amount is > 0)
        {
            lease.SetComponent(kind, from, amount.Value);
        }
    }

    private static DateOnly? NextDueDate(Lease lease, DateOnly today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        for (var i = 0; i < 24; i++, month = month.AddMonths(1))
        {
            var due = WorkingDays.NthWorkingDay(month.Year, month.Month, lease.DueOnWorkingDay);
            if (due >= today && due >= lease.Start && (lease.End is null || month <= lease.End))
            {
                return due;
            }
        }

        return null;
    }

    private IQueryable<Lease> WithDetails() =>
        db.Set<Lease>().Include(v => v.Parties).Include(v => v.Components).Include(v => v.People).Include(v => v.RentSteps).Include(v => v.ExtraRooms).AsSplitQuery();

    private Task<Result> WithLeaseAsync(Guid id, Action<Lease> action, CancellationToken cancellationToken) =>
        Command.ExecuteAsync(async () =>
        {
            var lease = await WithDetails().SingleOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException();
            action(lease);
            await db.SaveChangesAsync(cancellationToken);
            await modules.RefreshUsageAsync(cancellationToken);
            return id;
        }, Overlap);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => list.Contains(p.Id)).ToListAsync(cancellationToken);
        return people.ToDictionary(p => p.Id, p => p.DisplayName);
    }

    internal static string TenantText(Lease lease, IReadOnlyDictionary<Guid, string> names) =>
        string.Join(", ", lease.Tenant.Select(m => names.GetValueOrDefault(m.PersonId, "?")));

    private static ComponentVersion Entry(RentComponent b) => new(b.Id, b.Kind, b.Name, b.Validity, b.Amount);

    private static LeaseInput Data(Lease v) => new()
    {
        DueOnWorkingDay = v.DueOnWorkingDay,
        PaymentMethod = v.PaymentMethod,
        NoticePeriodMonths = v.NoticePeriodMonths,
        BillingType = v.BillingType,
        HeatingCostsConsumptionBased = v.HeatingCostsConsumptionBased,
        Deposit = v.Deposit,
        DepositNote = v.DepositNote,
        Notes = v.Notes,
    };

    private static void Apply(Lease v, LeaseInput d)
    {
        v.DueOnWorkingDay = d.DueOnWorkingDay;
        v.PaymentMethod = d.PaymentMethod;
        v.NoticePeriodMonths = d.NoticePeriodMonths;
        v.Deposit = d.Deposit;
        v.DepositNote = string.IsNullOrWhiteSpace(d.DepositNote) ? null : d.DepositNote.Trim();
        v.Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();
    }
}
