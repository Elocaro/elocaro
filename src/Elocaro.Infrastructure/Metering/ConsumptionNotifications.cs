using System.Globalization;
using Elocaro.Application.Communication;
using Elocaro.Application.Metering;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Metering;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Elocaro.Infrastructure.Metering;

/// <summary>
/// Monthly consumption information by mail (§ 6a HeizkostenV) to the tenants with a portal account: once per lease and month, for the
/// previous month, from the 3rd to the 25th as soon as the readings are in. The mail only gives the figures and points to the portal.
/// </summary>
internal sealed class ConsumptionNotifications(ElocaroDbContext db, IConsumptionInformation information, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <returns>Number of mails queued.</returns>
    public async Task<int> SendAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        if (today.Day is < 3 or > 25)
        {
            return 0;
        }

        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var last = month.AddMonths(1).AddDays(-1);
        var done = (await db.Set<ConsumptionInformationDispatch>().AsNoTracking().Where(d => d.Year == month.Year && d.Month == month.Month).Select(d => d.LeaseId).ToListAsync(cancellationToken)).ToHashSet();
        var leases = (await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).Where(v => v.RoomId == null).ToListAsync(cancellationToken))
            .Where(v => !done.Contains(v.Id) && v.Start <= last && (v.End is null || v.End >= month)).ToList();
        var link = app.Value.Link("/portal/verbrauch").ToString();
        var sent = 0;
        foreach (var lease in leases)
        {
            var monthly = await information.ForLeaseMonthAsync(lease.Id, month, cancellationToken);
            if (monthly is null || !monthly.Lines.Any(l => l.Current is not null))
            {
                // No own meters, or the readings are not in yet: try again tomorrow.
                continue;
            }

            var ids = lease.Tenant.Select(p => p.PersonId).ToList();
            var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
            foreach (var recipient in recipients)
            {
                await mail.EnqueueAsync(new MailRequest(
                    [recipient.Email!],
                    "consumption-information",
                    new
                    {
                        Salutation = LetterParts.SalutationOf([recipient]),
                        Month = month.ToString("MMMM yyyy", German),
                        Lines = monthly.Lines.Where(l => l.Current is not null).Select(Describe).ToList(),
                        Link = link,
                        SenderName = (await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken))?.Name ?? "Ihre Hausverwaltung",
                    },
                    Reference: $"consumption:{lease.Id}:{month:yyyy-MM}"), cancellationToken);
                sent++;
            }

            db.Add(new ConsumptionInformationDispatch(lease.Id, month.Year, month.Month, now));
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    private static object Describe(ConsumptionLine line)
    {
        var unit = UnitText(line.Unit);
        var details = new List<string>();
        AddComparison(details, "Vormonat", line.Current, line.Previous, unit);
        AddComparison(details, "Vorjahresmonat", line.Current, line.LastYear, unit);
        if (line.WeatherAdjustedChange is { } adjusted)
        {
            details.Add($"Witterungsbereinigt gegenüber dem Vorjahresmonat: {(adjusted > 0 ? "+" : "")}{adjusted:0} % (Verbrauch je Gradtag)");
        }

        if (line.Average is { } average && line.Own is { } own)
        {
            details.Add($"Durchschnitt der Wohnungen im Haus: {Number(average)} {unit} {line.BasisText} (Ihre Wohnung: {Number(own)} {unit} {line.BasisText})");
        }

        return new { line.Label, Current = $"{Number(line.Current!.Value)} {unit}", Details = details };
    }

    private static void AddComparison(List<string> details, string name, decimal? current, decimal? reference, string unit)
    {
        if (reference is not { } value)
        {
            return;
        }

        var change = ConsumptionComparison.ChangePercent(current, value);
        details.Add($"{name}: {Number(value)} {unit}{(change is { } percent ? $" ({(percent > 0 ? "+" : "")}{percent:0} %)" : "")}");
    }

    internal static string UnitText(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.CubicMeters => "m³",
        MeasurementUnit.KilowattHours => "kWh",
        MeasurementUnit.MegawattHours => "MWh",
        _ => "l",
    };

    private static string Number(decimal value) => value.ToString("#,##0.##", German);
}

[DisallowConcurrentExecution]
internal sealed class ConsumptionNotificationJob(ConsumptionNotifications notifications) : IJob
{
    public static readonly JobKey Key = new("consumption-information", "portal");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await notifications.SendAsync(cancellationToken);
    }
}

/// <summary>
/// Yearly reminder by mail to the tenants with a portal account and own meters to report the readings at the turn of the year:
/// from 15 December on, once per lease and year.
/// </summary>
internal sealed class ReadingReminders(ElocaroDbContext db, IMailSender mail, IOptions<ApplicationOptions> app, TimeProvider clock)
{
    /// <returns>Number of mails queued.</returns>
    public async Task<int> SendAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        if (today is not { Month: 12, Day: >= 15 })
        {
            return 0;
        }

        var done = (await db.Set<ReadingReminder>().AsNoTracking().Where(r => r.Year == today.Year).Select(r => r.LeaseId).ToListAsync(cancellationToken)).ToHashSet();
        var leases = (await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).ToListAsync(cancellationToken))
            .Where(v => !done.Contains(v.Id) && v.Start <= today && (v.End is null || v.End >= today)).ToList();
        var meters = await db.Set<Meter>().AsNoTracking().Where(m => m.RemovedOn == null && m.UnitId != null).Select(m => new { m.UnitId, m.RoomId }).ToListAsync(cancellationToken);
        var link = app.Value.Link("/portal/zaehler").ToString();
        var sent = 0;
        foreach (var lease in leases)
        {
            if (!meters.Any(m => m.UnitId == lease.UnitId && (lease.RoomId is null ? m.RoomId is null : m.RoomId == lease.RoomId)))
            {
                continue;
            }

            var ids = lease.Tenant.Select(p => p.PersonId).ToList();
            var recipients = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id) && p.UserId != null && p.Email != null && p.AnonymizedAt == null).ToListAsync(cancellationToken);
            foreach (var recipient in recipients)
            {
                await mail.EnqueueAsync(new MailRequest(
                    [recipient.Email!],
                    "reading-reminder",
                    new
                    {
                        Salutation = LetterParts.SalutationOf([recipient]),
                        today.Year,
                        Link = link,
                        SenderName = (await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken))?.Name ?? "Ihre Hausverwaltung",
                    },
                    Reference: $"reading-reminder:{lease.Id}:{today.Year}"), cancellationToken);
                sent++;
            }

            db.Add(new ReadingReminder(lease.Id, today.Year, now));
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }
}

[DisallowConcurrentExecution]
internal sealed class ReadingReminderJob(ReadingReminders reminders) : IJob
{
    public static readonly JobKey Key = new("meter-reading-reminder", "portal");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await reminders.SendAsync(cancellationToken);
    }
}
