using MietHahn.Domain.Common;
using MietHahn.Domain.Receipts;

namespace MietHahn.Domain.Leases;

/// <summary>How operating and heating costs are settled with the tenant.</summary>
public enum BillingType
{
    /// <summary>Monthly prepayments, annual utility cost statement.</summary>
    Prepayment = 0,

    /// <summary>Flat-rate operating costs without a statement (e.g. shared-flat room); internally actual/flat-rate comparison.</summary>
    FlatRate = 1,

    /// <summary>Utility costs are included in the rent (all-inclusive rent).</summary>
    Inclusive = 2,
}

public enum PaymentMethod
{
    BankTransfer = 0,
    DirectDebit = 1,
}

public enum PartyRole
{
    Tenant = 0,
    Guarantor = 1,
}

public enum TerminatingParty
{
    Tenant = 0,
    Landlord = 1,
    Mutual = 2,
}

public enum LeaseStatus
{
    /// <summary>Starts in the future.</summary>
    Future = 0,
    Active = 1,

    /// <summary>Terminated or fixed-term, end is known and lies in the future.</summary>
    Terminated = 2,
    Ended = 3,
}

/// <summary>
/// Lease for a rental object (unit without rooms or shared-flat room) with one or more tenants
/// (jointly and severally liable). Rent, prepayments and number of persons are time-dependent and never overwritten.
/// </summary>
public sealed class Lease : Entity
{
    private Lease()
    {
    }

    /// <param name="rentalUnitId">Id of the unit or room.</param>
    /// <param name="unitId">Unit of the rental object (for rooms, the shared-flat apartment).</param>
    public Lease(Guid rentalUnitId, Guid unitId, Guid? roomId, DateOnly start, DateOnly? fixedTermUntil = null)
    {
        if (fixedTermUntil < start)
        {
            throw new RuleViolationException("Das Ende der Befristung liegt vor dem Mietbeginn.");
        }

        RentalUnitId = rentalUnitId;
        UnitId = unitId;
        RoomId = roomId;
        FixedTermUntil = fixedTermUntil;
        Term = new Period(start, fixedTermUntil);
    }

    /// <summary>Unit or room id; only one lease may run per rental object on any given day.</summary>
    public Guid RentalUnitId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? RoomId { get; private set; }

    /// <summary>Actual term: start until fixed end or end after termination (<c>null</c> = open-ended).</summary>
    public Period Term { get; private set; }

    /// <summary>Fixed-term lease (§ 575 BGB) until this day.</summary>
    public DateOnly? FixedTermUntil { get; private set; }

    public DateOnly Start => Term.From;

    public DateOnly? End => Term.To;

    public DateOnly? TerminationReceivedOn { get; private set; }

    public TerminatingParty? TerminatedBy { get; private set; }

    /// <summary>Notice period for the tenant in months (statutory 3, § 573c Abs. 1 BGB).</summary>
    public int NoticePeriodMonths { get; set; } = 3;

    public BillingType BillingType { get; set; }

    /// <summary>Bill heating/hot water by consumption despite a flat rate (HeizkostenV takes precedence, see docs/fachlichkeit.md).</summary>
    public bool HeatingCostsConsumptionBased { get; set; }

    /// <summary>Due on the n-th working day of the month (statutory: 3rd, § 556b BGB).</summary>
    public int DueOnWorkingDay { get; set; } = 3;

    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Deposit: amount and note only (no deposit account management).</summary>
    public decimal? Deposit { get; set; }

    public string? DepositNote { get; set; }

    public string? Notes { get; set; }

    /// <summary>Cost types (keys) that are <b>not</b> agreed as allocable in this lease; by default all allocable types are agreed.</summary>
    public List<string> ExcludedCostTypes { get; private set; } = [];

    public RentModel RentModel { get; private set; }

    /// <summary>Index rent: month of the base index (first day of the month).</summary>
    public DateOnly? IndexBaseMonth { get; private set; }

    /// <summary>Index rent: index value of the base month (CPI, base 2020 = 100).</summary>
    public decimal? IndexBaseValue { get; private set; }

    public List<RentStep> RentSteps { get; private set; } = [];

    public List<LeaseParty> Parties { get; private set; } = [];

    public List<RentComponent> Components { get; private set; } = [];

    public List<OccupantCount> People { get; private set; } = [];

    public IEnumerable<LeaseParty> Tenant => Parties.Where(p => p.Role == PartyRole.Tenant).OrderBy(p => p.Order);

    /// <summary>Only leases with prepayments get a utility cost statement.</summary>
    public bool ReceivesOperatingCostStatement => BillingType == BillingType.Prepayment;

    /// <summary>Heating costs are billed by consumption (also with a flat rate, if so configured).</summary>
    public bool ReceivesHeatingCostStatement => BillingType == BillingType.Prepayment || HeatingCostsConsumptionBased;

    public LeaseStatus StatusAt(DateOnly day) =>
        day < Start ? LeaseStatus.Future
        : End is { } end && day > end ? LeaseStatus.Ended
        : End is not null ? LeaseStatus.Terminated
        : LeaseStatus.Active;

    public void AddParty(Guid personId, PartyRole role)
    {
        if (Parties.Any(p => p.PersonId == personId))
        {
            throw new RuleViolationException("Die Person ist bereits Vertragspartei.");
        }

        Parties.Add(new LeaseParty(Id, personId, role, Parties.Count == 0 ? 1 : Parties.Max(p => p.Order) + 1));
    }

    public void RemoveParty(Guid personId)
    {
        var party = Parties.SingleOrDefault(p => p.PersonId == personId) ?? throw new RuleViolationException("Die Person ist keine Vertragspartei.");
        if (party.Role == PartyRole.Tenant && Tenant.Count() == 1)
        {
            throw new RuleViolationException("Ein Vertrag braucht mindestens einen Mieter.");
        }

        Parties.Remove(party);
    }

    /// <summary>Sets a component (e.g. new base rent) from the effective date; the previous amount remains until the day before.</summary>
    public RentComponent SetComponent(RentComponentType kind, DateOnly from, decimal amount, string? name = null)
    {
        ValidateWithinLease(from);
        if (amount < 0)
        {
            throw new RuleViolationException("Beträge dürfen nicht negativ sein.");
        }

        var series = Components.Where(b => b.Kind == kind).ToList();
        var newItem = TimeSeries.CarryForward(series, from, g => new RentComponent(Id, kind, g, decimal.Round(amount, 2), name));
        Reconcile(Components, b => b.Kind == kind, series);
        return newItem;
    }

    /// <summary>Ends a component (e.g. parking space terminated) on the given day.</summary>
    public void EndComponent(RentComponentType kind, DateOnly lastDay)
    {
        var current = TimeSeries.At(Components.Where(b => b.Kind == kind), lastDay)
            ?? throw new RuleViolationException("Zu diesem Tag gibt es den Bestandteil nicht.");
        current.EndValidity(lastDay);
    }

    public OccupantCount SetOccupantCount(DateOnly from, int count)
    {
        ValidateWithinLease(from);
        if (count is < 1 or > 20)
        {
            throw new RuleViolationException("Bitte eine Personenanzahl zwischen 1 und 20 angeben.");
        }

        return TimeSeries.CarryForward(People, from, g => new OccupantCount(Id, g, count));
    }

    public IReadOnlyList<RentComponent> ComponentsAt(DateOnly day) =>
        Components.Where(b => b.Validity.Contains(day)).OrderBy(b => b.Kind).ToList();

    /// <summary>Total monthly rent (base rent + prepayments/flat rate + surcharges) on a day.</summary>
    public decimal MonthlyRentOn(DateOnly day) => ComponentsAt(day).Sum(b => b.Amount);

    public decimal? AmountAt(RentComponentType kind, DateOnly day) => TimeSeries.At(Components.Where(b => b.Kind == kind), day)?.Amount;

    /// <summary>Sets which cost types are not agreed in the lease (all others are allocable).</summary>
    public void SetExcludedCostTypes(IEnumerable<string> costTypes)
    {
        ArgumentNullException.ThrowIfNull(costTypes);
        var keys = costTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (keys.Any(k => CostTypes.Find(k) is not { Allocable: true }))
        {
            throw new RuleViolationException("Nur umlagefähige Kostenarten lassen sich im Vertrag ausschließen.");
        }

        ExcludedCostTypes = keys;
    }

    public bool IsAgreed(string costType) => !ExcludedCostTypes.Contains(costType);

    public int? OccupantCountOn(DateOnly day) => TimeSeries.At(People, day)?.Count;

    /// <summary>Records a termination; the lease end is the last day of the tenancy.</summary>
    public void Terminate(DateOnly receivedAt, TerminatingParty by, DateOnly effectiveOn)
    {
        if (effectiveOn < Start)
        {
            throw new RuleViolationException("Das Vertragsende liegt vor dem Mietbeginn.");
        }

        if (receivedAt > effectiveOn)
        {
            throw new RuleViolationException("Die Kündigung muss vor dem Vertragsende eingehen.");
        }

        TerminationReceivedOn = receivedAt;
        TerminatedBy = by;
        SetEnd(effectiveOn);
    }

    /// <summary>
    /// Revokes a recorded termination (e.g. withdrawn): open-ended again or until the fixed end.
    /// Components and number of persons that expired at the previous end continue.
    /// </summary>
    public void RevokeTermination()
    {
        if (TerminationReceivedOn is null)
        {
            throw new RuleViolationException("Es ist keine Kündigung erfasst.");
        }

        var previousEnd = End;
        TerminationReceivedOn = null;
        TerminatedBy = null;
        Term = new Period(Start, FixedTermUntil);
        foreach (var component in Components.Where(b => b.Validity.To == previousEnd))
        {
            component.SetTo(FixedTermUntil);
        }

        foreach (var entry in People.Where(p => p.Validity.To == previousEnd))
        {
            entry.SetTo(FixedTermUntil);
        }
    }

    /// <summary>
    /// Termination date for termination by the tenant (§ 573c Abs. 1 BGB): receipt by the 3rd working day of a month at the latest
    /// → end at the end of the month after next (with a 3-month period). Different periods shift accordingly.
    /// </summary>
    public static DateOnly TerminationDateForTenant(DateOnly incoming, int noticeMonths = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(noticeMonths, 1);
        var thirdWorkingDay = WorkingDays.NthWorkingDay(incoming.Year, incoming.Month, 3);
        var months = incoming <= thirdWorkingDay ? noticeMonths - 1 : noticeMonths;
        var month = new DateOnly(incoming.Year, incoming.Month, 1).AddMonths(months);
        return new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
    }

    /// <summary>
    /// Landlord's notice period (§ 573c Abs. 1 Satz 2 BGB): 3 months, after 5 years of tenancy 6, after 8 years 9 months.
    /// </summary>
    public int LandlordNoticePeriodMonths(DateOnly incoming)
    {
        var years = incoming.Year - Start.Year - (incoming < Start.AddYears(incoming.Year - Start.Year) ? 1 : 0);
        return years >= 8 ? 9 : years >= 5 ? 6 : 3;
    }

    /// <summary>Changes the rent model. When moving away from graduated rent, open steps must be removed.</summary>
    public void SetRentModel(RentModel model, DateOnly? indexBaseMonth = null, decimal? indexBaseValue = null)
    {
        if (model != RentModel.Graduated && RentSteps.Any(s => s.AppliedAt is null))
        {
            throw new RuleViolationException("Es gibt noch offene Staffeln. Bitte zuerst entfernen.");
        }

        if (model == RentModel.Index && (indexBaseMonth is null || indexBaseValue is not > 0))
        {
            throw new RuleViolationException("Für die Indexmiete bitte Monat und Wert des Ausgangsindex angeben.");
        }

        RentModel = model;
        IndexBaseMonth = model == RentModel.Index ? new DateOnly(indexBaseMonth!.Value.Year, indexBaseMonth.Value.Month, 1) : null;
        IndexBaseValue = model == RentModel.Index ? decimal.Round(indexBaseValue!.Value, 1) : null;
    }

    /// <summary>
    /// Adds a step (§ 557a BGB): the rent must remain unchanged for at least one year each time, the amount
    /// is stated as a monetary amount.
    /// </summary>
    public RentStep AddRentStep(DateOnly from, decimal baseRent)
    {
        if (RentModel != RentModel.Graduated)
        {
            throw new RuleViolationException("Staffeln gibt es nur bei einer Staffelmiete.");
        }

        ValidateWithinLease(from);
        if (baseRent <= 0)
        {
            throw new RuleViolationException("Bitte die neue Kaltmiete angeben.");
        }

        var dates = RentSteps.Select(s => s.From).Append(Start).Order().ToList();
        if (dates.Any(t => t != from && Math.Abs(MonthsBetween(t, from)) < 12))
        {
            throw new RuleViolationException("Zwischen zwei Staffeln (und ab Mietbeginn) muss die Miete mindestens ein Jahr unverändert bleiben.");
        }

        if (RentSteps.Any(s => s.From == from))
        {
            throw new RuleViolationException("Zu diesem Termin gibt es bereits eine Staffel.");
        }

        var rentStep = new RentStep(Id, from, decimal.Round(baseRent, 2));
        RentSteps.Add(rentStep);
        return rentStep;
    }

    public void RemoveRentStep(Guid rentStepId)
    {
        var rentStep = RentSteps.SingleOrDefault(s => s.Id == rentStepId) ?? throw new RuleViolationException("Die Staffel wurde nicht gefunden.");
        if (rentStep.AppliedAt is not null)
        {
            throw new RuleViolationException("Bereits übernommene Staffeln bleiben erhalten; die Miete bitte über die Bestandteile korrigieren.");
        }

        RentSteps.Remove(rentStep);
    }

    /// <summary>Adopts due steps as base rent (daily job). Returns the adopted steps.</summary>
    public IReadOnlyList<RentStep> ApplyDueSteps(DateOnly today, DateTimeOffset timestamp)
    {
        var applied = new List<RentStep>();
        foreach (var rentStep in RentSteps.Where(s => s.AppliedAt is null && s.From <= today && Term.Contains(s.From)).OrderBy(s => s.From))
        {
            SetComponent(RentComponentType.BaseRent, rentStep.From, rentStep.BaseRent);
            rentStep.MarkApplied(timestamp);
            applied.Add(rentStep);
        }

        return applied;
    }

    /// <summary>Adopts an index adjustment: new base rent from its effective date and new base index.</summary>
    public decimal AdjustIndex(DateOnly effectiveFrom, DateOnly cpiMonth, decimal cpiValue)
    {
        if (RentModel != RentModel.Index || IndexBaseValue is not { } baseValue)
        {
            throw new RuleViolationException("Der Vertrag ist keine Indexmiete.");
        }

        var lastChange = LastBaseRentChange() ?? Start;
        if (effectiveFrom < IndexedRent.EarliestEffectiveDate(lastChange))
        {
            throw new RuleViolationException(
                $"Die Miete muss mindestens ein Jahr unverändert bleiben: frühestens ab {IndexedRent.EarliestEffectiveDate(lastChange):dd.MM.yyyy}.");
        }

        var previous = AmountAt(RentComponentType.BaseRent, effectiveFrom) ?? throw new RuleViolationException("Zum Stichtag ist keine Kaltmiete hinterlegt.");
        var newItem = IndexedRent.NewRent(previous, baseValue, cpiValue);
        SetComponent(RentComponentType.BaseRent, effectiveFrom, newItem);
        IndexBaseMonth = new DateOnly(cpiMonth.Year, cpiMonth.Month, 1);
        IndexBaseValue = decimal.Round(cpiValue, 1);
        return newItem;
    }

    /// <summary>Start of the currently valid base rent, if it was changed after the lease start.</summary>
    public DateOnly? LastBaseRentChange(DateOnly? referenceDate = null)
    {
        var day = referenceDate ?? DateOnly.MaxValue;
        return Components.Where(b => b.Kind == RentComponentType.BaseRent && b.Validity.From > Start && b.Validity.From <= day)
            .Select(b => (DateOnly?)b.Validity.From).Max();
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);

    private void SetEnd(DateOnly end)
    {
        if (Components.Any(b => b.Validity.From > end) || People.Any(p => p.Validity.From > end))
        {
            throw new RuleViolationException("Nach dem neuen Vertragsende gibt es noch Mietbestandteile. Bitte zuerst diese korrigieren.");
        }

        Term = new Period(Start, end);
        foreach (var entry in Components.Concat<ITimeDependent>(People).Where(s => s.Validity.To is null || s.Validity.To > end))
        {
            entry.EndValidity(end);
        }
    }

    private void ValidateWithinLease(DateOnly day)
    {
        if (!Term.Contains(day))
        {
            throw new RuleViolationException($"Der Stichtag {day:dd.MM.yyyy} liegt außerhalb der Vertragslaufzeit ({Term}).");
        }
    }

    /// <summary>Applies additions/removals from the filtered sublist to the full list.</summary>
    private static void Reconcile<T>(List<T> total, Func<T, bool> filter, List<T> part)
    {
        total.RemoveAll(e => filter(e) && !part.Contains(e));
        total.AddRange(part.Where(e => !total.Contains(e)));
    }
}

public sealed class LeaseParty : Entity
{
    private LeaseParty()
    {
    }

    internal LeaseParty(Guid leaseId, Guid personId, PartyRole role, int order)
    {
        LeaseId = leaseId;
        PersonId = personId;
        Role = role;
        Order = order;
    }

    public Guid LeaseId { get; private set; }

    public Guid PersonId { get; private set; }

    public PartyRole Role { get; private set; }

    public int Order { get; private set; }
}

public enum RentComponentType
{
    BaseRent = 0,
    OperatingCostPrepayment = 1,
    HeatingCostPrepayment = 2,
    OperatingCostFlatRate = 3,
    ParkingSpace = 4,
    FurnishingSurcharge = 5,
    Miscellaneous = 9,
}

/// <summary>A monthly amount (base rent, prepayment, flat rate, surcharge) in a period.</summary>
public sealed class RentComponent : Entity, ITimeDependent
{
    private RentComponent()
    {
    }

    internal RentComponent(Guid leaseId, RentComponentType kind, Period validity, decimal amount, string? name)
    {
        LeaseId = leaseId;
        Kind = kind;
        Validity = validity;
        Amount = amount;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public Guid LeaseId { get; private set; }

    public RentComponentType Kind { get; private set; }

    public Period Validity { get; private set; }

    /// <summary>Monthly amount in euros.</summary>
    public decimal Amount { get; private set; }

    /// <summary>For "Sonstiges", e.g. "Gartennutzung".</summary>
    public string? Name { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);

    internal void SetTo(DateOnly? to) => Validity = new Period(Validity.From, to);
}

/// <summary>Number of persons living in the rental object (allocation by persons).</summary>
public sealed class OccupantCount : Entity, ITimeDependent
{
    private OccupantCount()
    {
    }

    internal OccupantCount(Guid leaseId, Period validity, int count)
    {
        LeaseId = leaseId;
        Validity = validity;
        Count = count;
    }

    public Guid LeaseId { get; private set; }

    public Period Validity { get; private set; }

    public int Count { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);

    internal void SetTo(DateOnly? to) => Validity = new Period(Validity.From, to);
}
