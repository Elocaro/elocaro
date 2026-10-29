using Elocaro.Domain.Common;

namespace Elocaro.Domain.Properties;

/// <summary>
/// A person owns a share of a property for a period (co-ownership by fractions). Without any owner the property belongs to
/// the landlord alone, as before. The shares of all owners valid on one day must not exceed 100 %.
/// </summary>
public sealed class PropertyOwner : Entity
{
    private PropertyOwner()
    {
    }

    public PropertyOwner(Guid propertyId, Guid personId, decimal sharePercent, DateOnly? validFrom, DateOnly? validTo)
    {
        PropertyId = propertyId;
        PersonId = personId;
        SharePercent = sharePercent;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Validate();
    }

    public Guid PropertyId { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>Share in percent (0.01 to 100, two decimals).</summary>
    public decimal SharePercent { get; private set; }

    /// <summary><c>null</c> = since ever.</summary>
    public DateOnly? ValidFrom { get; private set; }

    /// <summary><c>null</c> = until further notice.</summary>
    public DateOnly? ValidTo { get; private set; }

    public bool IsValidOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);

    /// <summary>Corrects person, share and period of the entry.</summary>
    public void Change(Guid personId, decimal sharePercent, DateOnly? validFrom, DateOnly? validTo)
    {
        PersonId = personId;
        SharePercent = sharePercent;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Validate();
    }

    /// <summary>Ends the ownership on <paramref name="day"/> (inclusive).</summary>
    public void End(DateOnly day)
    {
        if (ValidFrom is { } from && day < from)
        {
            throw new RuleViolationException("Das Ende liegt vor dem Beginn.");
        }

        ValidTo = day;
    }

    /// <summary>
    /// Do the shares of the existing owners plus the candidate exceed 100 % on any day of the candidate's period? Shares only
    /// change on the start of an entry, so the start of the candidate and the starts of the others inside it are enough.
    /// </summary>
    public static bool ExceedsFullShare(IReadOnlyCollection<PropertyOwner> existing, PropertyOwner candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);
        var days = existing.Where(o => o.ValidFrom is not null && candidate.IsValidOn(o.ValidFrom.Value)).Select(o => o.ValidFrom!.Value)
            .Append(candidate.ValidFrom ?? DateOnly.MinValue).Distinct();
        return days.Any(day => existing.Where(o => o.IsValidOn(day)).Sum(o => o.SharePercent) + candidate.SharePercent > 100m);
    }

    /// <summary>
    /// Share of a person in a calendar year in percent, weighted by days (50 % until June and 100 % from July is 75 %).
    /// <c>0</c> if the person did not own anything in the year.
    /// </summary>
    public static decimal YearShare(IEnumerable<PropertyOwner> owners, Guid personId, int year)
    {
        ArgumentNullException.ThrowIfNull(owners);
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var days = last.DayNumber - first.DayNumber + 1;
        var weighted = 0m;
        foreach (var owner in owners.Where(o => o.PersonId == personId))
        {
            var from = owner.ValidFrom is { } f && f > first ? f : first;
            var to = owner.ValidTo is { } t && t < last ? t : last;
            if (to >= from)
            {
                weighted += owner.SharePercent * (to.DayNumber - from.DayNumber + 1);
            }
        }

        return Math.Round(weighted / days, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Does the person already own the property in an overlapping period?</summary>
    public bool Overlaps(PropertyOwner other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return PersonId == other.PersonId
            && (ValidTo is null || other.ValidFrom is null || other.ValidFrom <= ValidTo)
            && (other.ValidTo is null || ValidFrom is null || ValidFrom <= other.ValidTo);
    }

    private void Validate()
    {
        if (SharePercent is <= 0 or > 100 || decimal.Round(SharePercent, 2) != SharePercent)
        {
            throw new RuleViolationException("Der Anteil muss zwischen 0,01 und 100 % liegen (höchstens zwei Nachkommastellen).");
        }

        if (ValidFrom is { } from && ValidTo is { } to && to < from)
        {
            throw new RuleViolationException("Das Ende der Gültigkeit liegt vor dem Beginn.");
        }
    }
}
