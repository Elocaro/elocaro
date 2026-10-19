using MietHahn.Domain.Common;

namespace MietHahn.Domain.Leases;

/// <summary>Wie Betriebs- und Heizkosten mit dem Mieter abgerechnet werden.</summary>
public enum BillingType
{
    /// <summary>Monatliche Vorauszahlungen, jährliche Nebenkostenabrechnung.</summary>
    Prepayment = 0,

    /// <summary>Betriebskostenpauschale ohne Abrechnung (z. B. WG-Zimmer); intern Ist/Pauschale-Vergleich.</summary>
    FlatRate = 1,

    /// <summary>Nebenkosten sind in der Miete enthalten (Inklusivmiete).</summary>
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
    /// <summary>Beginnt in der Zukunft.</summary>
    Future = 0,
    Active = 1,

    /// <summary>Gekündigt oder befristet, Ende steht fest und liegt in der Zukunft.</summary>
    Terminated = 2,
    Ended = 3,
}

/// <summary>
/// Mietvertrag über ein Mietobjekt (Einheit ohne Zimmer oder WG-Zimmer) mit einem oder mehreren Mietern
/// (gesamtschuldnerisch). Miete, Vorauszahlungen und Personenanzahl sind zeitabhängig und werden nie überschrieben.
/// </summary>
public sealed class Lease : Entity
{
    private Lease()
    {
    }

    /// <param name="rentalUnitId">Id der Einheit bzw. des Zimmers.</param>
    /// <param name="unitId">Einheit des Mietobjekts (bei Zimmern die WG-Wohnung).</param>
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

    /// <summary>Einheit- oder Zimmer-Id; je Mietobjekt darf zu jedem Tag nur ein Vertrag laufen.</summary>
    public Guid RentalUnitId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? RoomId { get; private set; }

    /// <summary>Tatsächliche Laufzeit: Beginn bis Befristung bzw. Ende nach Kündigung (<c>null</c> = unbefristet).</summary>
    public Period Term { get; private set; }

    /// <summary>Zeitmietvertrag (§ 575 BGB) bis zu diesem Tag.</summary>
    public DateOnly? FixedTermUntil { get; private set; }

    public DateOnly Start => Term.From;

    public DateOnly? End => Term.To;

    public DateOnly? TerminationReceivedOn { get; private set; }

    public TerminatingParty? TerminatedBy { get; private set; }

    /// <summary>Kündigungsfrist für den Mieter in Monaten (gesetzlich 3, § 573c Abs. 1 BGB).</summary>
    public int NoticePeriodMonths { get; set; } = 3;

    public BillingType BillingType { get; set; }

    /// <summary>Heizung/Warmwasser trotz Pauschale verbrauchsabhängig abrechnen (HeizkostenV hat Vorrang, siehe docs/fachlichkeit.md).</summary>
    public bool HeatingCostsConsumptionBased { get; set; }

    /// <summary>Fällig am n-ten Werktag des Monats (gesetzlich: 3., § 556b BGB).</summary>
    public int DueOnWorkingDay { get; set; } = 3;

    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Kaution: nur Betrag und Notiz (keine Kautionskontoverwaltung).</summary>
    public decimal? Deposit { get; set; }

    public string? DepositNote { get; set; }

    public string? Notes { get; set; }

    public RentModel RentModel { get; private set; }

    /// <summary>Indexmiete: Monat des Ausgangsindex (erster Tag des Monats).</summary>
    public DateOnly? IndexBaseMonth { get; private set; }

    /// <summary>Indexmiete: Indexstand des Ausgangsmonats (VPI, Basis 2020 = 100).</summary>
    public decimal? IndexBaseValue { get; private set; }

    public List<RentStep> RentSteps { get; private set; } = [];

    public List<LeaseParty> Parties { get; private set; } = [];

    public List<RentComponent> Components { get; private set; } = [];

    public List<OccupantCount> People { get; private set; } = [];

    public IEnumerable<LeaseParty> Tenant => Parties.Where(p => p.Role == PartyRole.Tenant).OrderBy(p => p.Order);

    /// <summary>Nur Verträge mit Vorauszahlungen bekommen eine Nebenkostenabrechnung.</summary>
    public bool ReceivesOperatingCostStatement => BillingType == BillingType.Prepayment;

    /// <summary>Heizkosten werden verbrauchsabhängig abgerechnet (auch bei Pauschale, wenn so konfiguriert).</summary>
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

    /// <summary>Setzt einen Bestandteil (z. B. neue Kaltmiete) ab Stichtag; der bisherige Betrag bleibt bis zum Vortag.</summary>
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

    /// <summary>Beendet einen Bestandteil (z. B. Stellplatz gekündigt) zum angegebenen Tag.</summary>
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

    /// <summary>Monatliche Gesamtmiete (Kaltmiete + Vorauszahlungen/Pauschale + Zuschläge) an einem Tag.</summary>
    public decimal MonthlyRentOn(DateOnly day) => ComponentsAt(day).Sum(b => b.Amount);

    public decimal? AmountAt(RentComponentType kind, DateOnly day) => TimeSeries.At(Components.Where(b => b.Kind == kind), day)?.Amount;

    public int? OccupantCountOn(DateOnly day) => TimeSeries.At(People, day)?.Count;

    /// <summary>Erfasst eine Kündigung; das Vertragsende ist der letzte Tag des Mietverhältnisses.</summary>
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
    /// Hebt eine erfasste Kündigung auf (z. B. zurückgenommen): wieder unbefristet bzw. bis zur Befristung.
    /// Bestandteile und Personenanzahl, die zum bisherigen Ende ausliefen, laufen weiter.
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
    /// Kündigungstermin bei Kündigung durch den Mieter (§ 573c Abs. 1 BGB): Zugang spätestens am 3. Werktag eines Monats
    /// → Ende mit Ablauf des übernächsten Monats (bei 3 Monaten Frist). Abweichende Fristen verschieben entsprechend.
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
    /// Kündigungsfrist des Vermieters (§ 573c Abs. 1 Satz 2 BGB): 3 Monate, nach 5 Jahren Mietdauer 6, nach 8 Jahren 9 Monate.
    /// </summary>
    public int LandlordNoticePeriodMonths(DateOnly incoming)
    {
        var years = incoming.Year - Start.Year - (incoming < Start.AddYears(incoming.Year - Start.Year) ? 1 : 0);
        return years >= 8 ? 9 : years >= 5 ? 6 : 3;
    }

    /// <summary>Wechselt das Mietmodell. Beim Wechsel weg von der Staffelmiete müssen offene Staffeln entfernt sein.</summary>
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
    /// Fügt eine Staffel hinzu (§ 557a BGB): Die Miete muss jeweils mindestens ein Jahr unverändert bleiben, der Betrag
    /// ist als Geldbetrag ausgewiesen.
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

    /// <summary>Übernimmt fällige Staffeln als Kaltmiete (täglicher Job). Liefert die übernommenen Staffeln.</summary>
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

    /// <summary>Übernimmt eine Indexanpassung: neue Kaltmiete ab Wirksamkeit und neuer Ausgangsindex.</summary>
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

    /// <summary>Beginn der aktuell gültigen Kaltmiete, falls sie nach Mietbeginn geändert wurde.</summary>
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

    /// <summary>Überträgt Hinzufügen/Entfernen aus der gefilterten Teilliste auf die Gesamtliste.</summary>
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

/// <summary>Ein monatlicher Betrag (Kaltmiete, Vorauszahlung, Pauschale, Zuschlag) in einem Zeitraum.</summary>
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

    /// <summary>Monatsbetrag in Euro.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Für „Sonstiges“, z. B. „Gartennutzung“.</summary>
    public string? Name { get; private set; }

    public void EndValidity(DateOnly lastDay) => Validity = Validity.EndOn(lastDay);

    internal void SetTo(DateOnly? to) => Validity = new Period(Validity.From, to);
}

/// <summary>Anzahl der im Mietobjekt lebenden Personen (Umlage nach Personen).</summary>
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
