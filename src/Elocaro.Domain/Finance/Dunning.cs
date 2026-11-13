using Elocaro.Domain.Common;

namespace Elocaro.Domain.Finance;

/// <summary>Dunning levels: friendly payment reminder, then first and second notice.</summary>
public enum DunningLevel
{
    Reminder = 1,
    FirstNotice = 2,
    SecondNotice = 3,
}

public enum DunningStatus
{
    /// <summary>Suggested by the job, waiting for approval. Nothing has been sent.</summary>
    Proposed = 0,

    /// <summary>Approved: letter created (and sent by email if chosen).</summary>
    Issued = 1,

    /// <summary>Rejected by the user; no new proposal for a while.</summary>
    Dismissed = 2,
}

/// <summary>Configurable dunning parameters (taken from the finance settings).</summary>
/// <param name="GraceDays">A claim counts as arrears when it is more than this many days overdue.</param>
/// <param name="DaysUntilFirstNotice">Days between the payment reminder and the first notice.</param>
/// <param name="DaysUntilSecondNotice">Days between the first and the second notice.</param>
/// <param name="DeadlineDays">Payment period in the letter, counted from the letter date.</param>
/// <param name="MinimumAmount">No dunning for arrears below this amount.</param>
/// <param name="ChargeInterest">Whether default interest is demanded from the first notice on.</param>
/// <param name="BaseRate">Basiszinssatz in percent p.a. (may be negative); without it no interest is calculated.</param>
/// <param name="InterestMarginPoints">Margin over the base rate in percentage points.</param>
public sealed record DunningRules(
    int GraceDays,
    int DaysUntilFirstNotice,
    int DaysUntilSecondNotice,
    int DeadlineDays,
    decimal MinimumAmount,
    decimal ReminderFee,
    decimal FirstNoticeFee,
    decimal SecondNoticeFee,
    bool ChargeInterest,
    decimal? BaseRate,
    decimal InterestMarginPoints)
{
    public static readonly DunningRules Default = new(5, 14, 14, 7, 5m, 0m, 0m, 0m, false, null, 5m);

    public decimal FeeFor(DunningLevel level) => level switch
    {
        DunningLevel.Reminder => ReminderFee,
        DunningLevel.FirstNotice => FirstNoticeFee,
        _ => SecondNoticeFee,
    };

    /// <summary>Annual default interest rate in percent (base rate + margin), never below 0; null without a base rate.</summary>
    public decimal? InterestRate => ChargeInterest && BaseRate is { } basis ? Math.Max(0m, basis + InterestMarginPoints) : null;
}

/// <summary>An overdue claim as input for the dunning planning.</summary>
/// <param name="Open">Still unpaid part after allocation of the payments.</param>
/// <param name="Settlements">Part payments already allocated to the claim; the interest runs on the settled part until the day of payment.</param>
public sealed record DunningClaim(Guid ChargeId, ChargeType Kind, string Text, DateOnly Due, decimal Amount, decimal Open, IReadOnlyList<Settlement>? Settlements = null)
{
    /// <summary>Only principal claims are dunned; fees and interest are not themselves a reason (no interest on interest).</summary>
    public bool IsPrincipal => Charge.RankOf(Kind) == AllocationRank.PrincipalClaim;
}

/// <summary>Part of a claim that a payment settled on <paramref name="Date"/>.</summary>
public readonly record struct Settlement(DateOnly Date, decimal Amount);

/// <summary>Earlier dunning (issued or dismissed) of a tenancy as input for the planning.</summary>
public sealed record DunningHistoryEntry(DunningLevel Level, DunningStatus Status, DateOnly Date);

/// <summary>Result of the planning: which level is due, for which claims, with fee and interest.</summary>
public sealed record DunningPlan(
    DunningLevel Level,
    IReadOnlyList<DunningClaim> Claims,
    decimal Principal,
    decimal Fee,
    decimal Interest,
    DateOnly OldestDue)
{
    public int DaysOverdue(DateOnly today) => today.DayNumber - OldestDue.DayNumber;
}

/// <summary>
/// Pure dunning logic: which level is due for a tenancy? A proposal exists only while arrears exist
/// (a payment before approval removes it) and only for the next level once its waiting period has passed.
/// </summary>
public static class DunningPlanner
{
    /// <summary>A dismissed proposal is not made again for this many days.</summary>
    public const int SnoozeDays = 14;

    public static DunningPlan? Plan(
        DunningRules rules,
        DateOnly today,
        IReadOnlyCollection<DunningClaim> claims,
        IReadOnlyCollection<DunningHistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(history);

        var arrears = claims
            .Where(c => c.IsPrincipal && c.Open > 0 && today.DayNumber - c.Due.DayNumber > rules.GraceDays)
            .OrderBy(c => c.Due).ThenBy(c => c.Text, StringComparer.Ordinal)
            .ToList();
        var principal = arrears.Sum(c => c.Open);
        if (arrears.Count == 0 || principal < rules.MinimumAmount)
        {
            return null;
        }

        if (history.Any(h => h.Status == DunningStatus.Dismissed && h.Date.AddDays(SnoozeDays) > today))
        {
            return null;
        }

        var level = DunningLevel.Reminder;
        var last = history.Where(h => h.Status == DunningStatus.Issued).OrderByDescending(h => h.Date).ThenByDescending(h => h.Level).FirstOrDefault();

        // Escalate only if arrears already existed at the last dunning; new arrears start again with the reminder.
        if (last is not null && arrears.Any(c => c.Due < last.Date))
        {
            if (last.Level == DunningLevel.SecondNotice)
            {
                return null;
            }

            var next = last.Level + 1;
            var wait = next == DunningLevel.FirstNotice ? rules.DaysUntilFirstNotice : rules.DaysUntilSecondNotice;
            if (today < last.Date.AddDays(wait))
            {
                return null;
            }

            level = next;
        }

        var fee = rules.FeeFor(level);
        var interest = level >= DunningLevel.FirstNotice ? DefaultInterest.Total(rules, arrears, today) : 0m;
        return new DunningPlan(level, arrears, principal, fee, interest, arrears[0].Due);
    }
}

/// <summary>Default interest under § 288 BGB (base rate + margin), actual/365 days, simple interest.</summary>
public static class DefaultInterest
{
    /// <summary>Interest on the open amount from the day after the due date up to and including <paramref name="until"/>.</summary>
    public static decimal Calculate(decimal open, DateOnly due, DateOnly until, decimal annualPercent)
    {
        var days = until.DayNumber - due.DayNumber;
        if (open <= 0 || days <= 0 || annualPercent <= 0)
        {
            return 0m;
        }

        return decimal.Round(open * annualPercent / 100m * days / 365m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Interest on a claim that was paid in parts: the full amount runs from the day after the due date until the first payment,
    /// then the remainder until the next one, and so on. A payment day counts as the last day of interest. Rounded to the cent once.
    /// </summary>
    public static decimal Calculate(decimal amount, DateOnly due, IEnumerable<Settlement> settlements, DateOnly until, decimal annualPercent)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        if (amount <= 0 || annualPercent <= 0)
        {
            return 0m;
        }

        var remaining = amount;
        var from = due;
        var amountDays = 0m;
        foreach (var settlement in settlements.Where(s => s.Amount > 0).OrderBy(s => s.Date))
        {
            var end = settlement.Date < until ? settlement.Date : until;
            if (end.DayNumber > from.DayNumber)
            {
                amountDays += remaining * (end.DayNumber - from.DayNumber);
                from = end;
            }

            remaining -= settlement.Amount;
            if (remaining <= 0)
            {
                return Round(amountDays, annualPercent);
            }
        }

        if (until.DayNumber > from.DayNumber)
        {
            amountDays += remaining * (until.DayNumber - from.DayNumber);
        }

        return Round(amountDays, annualPercent);
    }

    /// <summary>Sum over the principal claims (each rounded to the cent); 0 if interest is switched off or the base rate is missing.</summary>
    public static decimal Total(DunningRules rules, IEnumerable<DunningClaim> claims, DateOnly until)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(claims);
        return rules.InterestRate is { } rate
            ? claims.Where(c => c.IsPrincipal).Sum(c => c.Settlements is { Count: > 0 } settlements
                ? Calculate(c.Amount, c.Due, settlements, until, rate)
                : Calculate(c.Open, c.Due, until, rate))
            : 0m;
    }

    private static decimal Round(decimal amountDays, decimal annualPercent) =>
        decimal.Round(amountDays * annualPercent / 100m / 365m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Hint on relevance for termination without notice (§ 543 Abs. 2 Satz 1 Nr. 3 BGB). Information only, no automatism.
/// </summary>
/// <param name="TwoConsecutiveTerms">Unpaid rent in two consecutive terms exceeds one monthly rent.</param>
/// <param name="TwoMonthlyRents">The total arrears reach two monthly rents.</param>
public sealed record TerminationRelevance(bool TwoConsecutiveTerms, bool TwoMonthlyRents)
{
    public static readonly TerminationRelevance None = new(false, false);

    public bool Any => TwoConsecutiveTerms || TwoMonthlyRents;

    /// <param name="monthlyRent">Monthly rent including prepayments (what is owed per term).</param>
    /// <param name="overdueRent">Unpaid open parts of overdue monthly rents with their due dates.</param>
    public static TerminationRelevance Check(decimal monthlyRent, IEnumerable<(DateOnly Due, decimal Open)> overdueRent)
    {
        ArgumentNullException.ThrowIfNull(overdueRent);
        if (monthlyRent <= 0)
        {
            return None;
        }

        var perTerm = overdueRent.Where(r => r.Open > 0)
            .GroupBy(r => r.Due.Year * 12 + r.Due.Month - 1)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Open));
        var total = perTerm.Values.Sum();
        var consecutive = perTerm.Any(t => perTerm.TryGetValue(t.Key + 1, out var next) && t.Value + next > monthlyRent);
        return new TerminationRelevance(consecutive, total >= 2 * monthlyRent);
    }
}

/// <summary>
/// A dunning of a tenancy: first a proposal of the job (<see cref="DunningStatus.Proposed"/>), after approval
/// <see cref="DunningStatus.Issued"/> with level, date and the stored letter. Nothing is sent without approval.
/// </summary>
public sealed class Dunning : Entity
{
    private Dunning()
    {
    }

    public Dunning(Guid leaseId, DunningLevel level, DateOnly proposedOn, decimal principal, decimal fee, decimal interest, DateOnly oldestDue, bool terminationRelevant)
    {
        LeaseId = leaseId;
        Level = level;
        ProposedOn = proposedOn;
        Status = DunningStatus.Proposed;
        OldestDue = oldestDue;
        Update(level, principal, fee, interest, oldestDue, terminationRelevant);
    }

    public Guid LeaseId { get; private set; }

    public DunningLevel Level { get; private set; }

    public DunningStatus Status { get; private set; }

    /// <summary>Day on which the job created the proposal.</summary>
    public DateOnly ProposedOn { get; private set; }

    /// <summary>Due date of the oldest claim in arrears.</summary>
    public DateOnly OldestDue { get; private set; }

    /// <summary>Open principal at the last check (at approval: as demanded in the letter).</summary>
    public decimal Principal { get; private set; }

    public decimal Fee { get; private set; }

    public decimal Interest { get; private set; }

    /// <summary>The arrears reached the threshold for termination relevance (hint only).</summary>
    public bool TerminationRelevant { get; private set; }

    /// <summary>Day of approval (letter date); at dismissal the day of rejection.</summary>
    public DateOnly? DecidedOn { get; private set; }

    public DateOnly? Deadline { get; private set; }

    /// <summary>The letter in the file store (after approval).</summary>
    public Guid? FileId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public bool SentByMail { get; private set; }

    public Guid? MailId { get; private set; }

    public Guid? FeeChargeId { get; private set; }

    public Guid? InterestChargeId { get; private set; }

    /// <summary>Adjusts the proposal to the current arrears. Returns whether anything changed.</summary>
    public bool Update(DunningLevel level, decimal principal, decimal fee, decimal interest, DateOnly oldestDue, bool terminationRelevant)
    {
        EnsureProposed();
        if (Level == level && Principal == principal && Fee == fee && Interest == interest && OldestDue == oldestDue && TerminationRelevant == terminationRelevant)
        {
            return false;
        }

        Level = level;
        Principal = principal;
        Fee = fee;
        Interest = interest;
        OldestDue = oldestDue;
        TerminationRelevant = terminationRelevant;
        return true;
    }

    public void Issue(DateOnly day, DateOnly deadline, decimal principal, decimal fee, decimal interest, Guid fileId, Guid? documentId, Guid? mailId, Guid? feeChargeId, Guid? interestChargeId)
    {
        EnsureProposed();
        if (deadline < day)
        {
            throw new RuleViolationException("Die Zahlungsfrist darf nicht vor dem Datum der Mahnung liegen.");
        }

        Status = DunningStatus.Issued;
        DecidedOn = day;
        Deadline = deadline;
        Principal = principal;
        Fee = fee;
        Interest = interest;
        FileId = fileId;
        DocumentId = documentId;
        MailId = mailId;
        SentByMail = mailId is not null;
        FeeChargeId = feeChargeId;
        InterestChargeId = interestChargeId;
    }

    public void Dismiss(DateOnly day)
    {
        EnsureProposed();
        Status = DunningStatus.Dismissed;
        DecidedOn = day;
    }

    private void EnsureProposed()
    {
        if (Status != DunningStatus.Proposed)
        {
            throw new RuleViolationException("Diese Mahnung wurde bereits entschieden und lässt sich nicht mehr ändern.");
        }
    }
}

/// <summary>Dunning hold of a tenancy (e.g. instalment agreement): while active, no proposals are made.</summary>
public sealed class DunningHold : Entity
{
    private DunningHold()
    {
        Reason = "";
    }

    public DunningHold(Guid leaseId, string reason, DateOnly? until)
    {
        LeaseId = leaseId;
        Reason = reason;
        Until = until;
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RuleViolationException("Bitte einen Grund für die Mahnsperre angeben (z. B. Ratenvereinbarung).");
        }
    }

    public Guid LeaseId { get; private set; }

    public string Reason { get; private set; }

    /// <summary>Last day of the hold; empty = until cancelled.</summary>
    public DateOnly? Until { get; private set; }

    public bool IsActiveOn(DateOnly day) => Until is null || day <= Until;

    public void End(DateOnly day)
    {
        if (!IsActiveOn(day))
        {
            return;
        }

        Until = day.AddDays(-1);
    }
}

/// <summary>German names of the dunning levels (letters, mails, display).</summary>
public static class DunningLevelNames
{
    public static string Name(this DunningLevel level) => level switch
    {
        DunningLevel.Reminder => "Zahlungserinnerung",
        DunningLevel.FirstNotice => "1. Mahnung",
        _ => "2. Mahnung",
    };
}
