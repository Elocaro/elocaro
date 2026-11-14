using System.Text.RegularExpressions;
using Elocaro.Application.Finance;
using Elocaro.Domain.Finance;
using Elocaro.Web.Display;

namespace Elocaro.Web.Components.Pages.Admin.Finance;

/// <summary>A row of the booking list: a booking, a month of rent (the components together) or one component below its month.</summary>
public sealed record AccountRow(AccountEntry Entry, IReadOnlyList<AccountEntry>? Parts = null, bool IsPart = false)
{
    public bool IsGroup => Parts is not null;
}

/// <summary>How the tenant account shows its bookings: rent components of one due date grouped to a month, filtered by year and open items.</summary>
public static partial class AccountRows
{
    /// <summary>
    /// Newest first. The rent components that fall due on the same day (cold rent, operating cost and heating prepayment) are one
    /// row "Monatsmiete 11/2026" with the sum and the balance after the last component; the expanded months show the components below.
    /// </summary>
    public static IReadOnlyList<AccountRow> Visible(TenantAccount account, int? year, bool onlyOpen, ISet<DateOnly> expandedMonths)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(expandedMonths);
        var rentByDay = account.Entries.Where(IsRentCharge).GroupBy(e => e.Date).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<AccountRow>();
        foreach (var entry in account.Entries)
        {
            if (!IsRentCharge(entry) || !rentByDay.TryGetValue(entry.Date, out var parts))
            {
                rows.Add(new(entry));
            }
            else if (entry == parts[^1])
            {
                rows.Add(new(MonthOf(parts), parts));
            }
        }

        rows = [.. rows.Where(r => (year is null || r.Entry.Date.Year == year) && (!onlyOpen || r.Entry.Open > 0))];
        rows.Reverse();
        return [.. rows.SelectMany(r => r.IsGroup && expandedMonths.Contains(r.Entry.Date) ? r.Parts!.Select(p => new AccountRow(p, IsPart: true)).Prepend(r) : [r])];
    }

    /// <summary>What a payment of the amount due is for: the months of the open charges that are due ("Miete November 2026").</summary>
    public static string? PurposeOfOpenCharges(TenantAccount account, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(account);
        var due = account.Entries.Where(e => e.EntryType == EntryType.Charge && e.Open > 0 && e.Date <= today).ToList();
        if (due.Count == 0)
        {
            return null;
        }

        var (first, last) = (due.Min(e => e.Date), due.Max(e => e.Date));
        var what = due.All(e => e.Kind == ChargeType.Rent) ? "Miete" : "Zahlung";
        return first.Year == last.Year && first.Month == last.Month ? $"{what} {Format.Month(first)}" : $"{what} {Format.Month(first)} bis {Format.Month(last)}";
    }

    private static bool IsRentCharge(AccountEntry entry) => entry.EntryType == EntryType.Charge && entry.Kind == ChargeType.Rent;

    private static AccountEntry MonthOf(List<AccountEntry> parts)
    {
        // "Kaltmiete 11/2026", "Betriebskostenvorauszahlung 11/2026" → "Monatsmiete 11/2026"; a part-month stays recognisable
        var months = parts.Select(p => MonthPattern().Match(p.Text).Value).Distinct().ToList();
        var label = $"Monatsmiete {(months.Count == 1 && months[0].Length > 0 ? months[0] : Format.Month(parts[0].Date))}";
        if (parts.Any(p => p.Text.Contains("(anteilig", StringComparison.Ordinal)))
        {
            label += " (anteilig)";
        }

        var last = parts[^1];
        return new AccountEntry(parts[0].Id, EntryType.Charge, parts[0].Date, label, ChargeType.Rent, parts.Sum(p => p.Debit), 0m, last.Balance,
            parts.Sum(p => p.Open ?? 0m), true, null);
    }

    [GeneratedRegex(@"\d{2}/\d{4}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MonthPattern();
}
