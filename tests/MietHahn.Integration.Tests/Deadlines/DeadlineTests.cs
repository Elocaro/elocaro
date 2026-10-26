using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Deadlines;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Tax;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Deadlines;
using MietHahn.Domain.Metering;
using MietHahn.Infrastructure.Deadlines;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Deadlines;

public class DeadlineTests(PostgresFixture postgres) : IAsyncLifetime
{
    // Start time of the test environment: 1 March 2026
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));
    private static readonly ClaimsPrincipal NoRights = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager)], "test"));

    private InfrastructureEnvironment environment = null!;
    private Guid propertyId;
    private Guid leaseId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres);
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        propertyId = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(propertyId, new UnitInput { Name = "Wohnung 1" }, Ct));
        var tenant = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt" }, null, Ct));
        leaseId = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(
            new LeaseCreation { RentalUnitId = apartment, Start = new DateOnly(2025, 4, 1), FixedTermUntil = new DateOnly(2026, 5, 31), TenantIds = [tenant], BaseRent = 650m }, Ct));
        await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateFirstAdminAsync("admin@example.de", "Admin", "Langes-Passwort-2026", Ct);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Derived_deadlines_appear_without_manual_entry()
    {
        await WithAsync(async (IDeadlines _, IServiceProvider sp) =>
        {
            var loan = Validate(await sp.GetRequiredService<ILoanService>().CreateAsync(new LoanInput
            {
                PropertyId = propertyId,
                Bank = "Sparkasse",
                Principal = 200000m,
                DisbursementDate = new DateOnly(2020, 1, 1),
                FirstPaymentDate = new DateOnly(2020, 2, 1),
                InterestRatePercent = 1.5m,
                Instalment = 900m,
                FixedUntil = new DateOnly(2030, 1, 31),
            }, Ct));
            return loan;
        });

        var entries = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.ListAsync(Admin, new DateOnly(2031, 1, 1), Ct));

        Assert.Contains(entries, e => e.Key == $"lease-end:{leaseId}" && e.Due == new DateOnly(2026, 5, 31) && e.Title.StartsWith("Befristung endet", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Key.StartsWith("loan-fixed:", StringComparison.Ordinal) && e.Due == new DateOnly(2030, 1, 31));
        Assert.Equal(entries.OrderBy(e => e.Due).Select(e => e.Key), entries.Select(e => e.Key));

        var limited = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.ListAsync(NoRights, new DateOnly(2031, 1, 1), Ct));
        Assert.DoesNotContain(limited, e => e.Right == AccessRight.Finance);
    }

    [Fact]
    public async Task A_reading_at_the_tenant_change_is_due_when_the_unit_has_meters()
    {
        var entries = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.ListAsync(Admin, new DateOnly(2031, 1, 1), Ct));
        Assert.DoesNotContain(entries, e => e.Key.StartsWith("reading:", StringComparison.Ordinal));

        await WithAsync(async (IDeadlines _, IServiceProvider sp) =>
        {
            var unit = (await sp.GetRequiredService<IPropertyService>().GetAsync(propertyId, Ct))!.Units[0].Id;
            return Validate(await sp.GetRequiredService<IMeterService>().CreateAsync(
                propertyId,
                new MeterCreation { Data = new MeterInput { Type = MeterType.ColdWater, Name = "Kaltwasser", Number = "WZ-1", AssignmentId = unit }, InstalledOn = new DateOnly(2025, 4, 1), InitialReading = 0m },
                Ct));
        });

        entries = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.ListAsync(Admin, new DateOnly(2031, 1, 1), Ct));
        var reading = Assert.Single(entries, e => e.Key == $"reading:{leaseId}:Auszug");
        Assert.Equal(new DateOnly(2026, 5, 31), reading.Due);
        Assert.Contains($"/verwaltung/objekte/{propertyId}/ablesung?einheit=", reading.Link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completing_a_task_records_the_proof_and_moves_the_date()
    {
        var id = Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CreateTaskAsync(
            new TaskInput { Title = "Rauchmelder", Kind = TaskKind.SmokeDetectors, PropertyId = propertyId, DueOn = new DateOnly(2026, 3, 15), IntervalMonths = 12 }, Ct)));

        var tooEarly = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CompleteTaskAsync(id, new CompletionInput { DoneOn = new DateOnly(2026, 3, 2) }, Ct));
        Assert.False(tooEarly.Succeeded);

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 16, 9, 0, 0, TimeSpan.Zero));
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CompleteTaskAsync(id, new CompletionInput { DoneOn = new DateOnly(2026, 3, 16), Note = "alle Melder getauscht" }, Ct)));

        var detail = (await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.TaskAsync(id, Ct)))!;
        Assert.Equal(new DateOnly(2027, 3, 15), detail.Task.DueOn);
        Assert.Equal(new DateOnly(2026, 3, 16), detail.Task.LastDoneOn);
        Assert.Equal("alle Melder getauscht", detail.Completions.Single().Note);

        var missing = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CompleteTaskAsync(id, new CompletionInput { DoneOn = new DateOnly(2026, 3, 16), DocumentId = Guid.NewGuid() }, Ct));
        Assert.False(missing.Succeeded);
    }

    [Fact]
    public async Task Reminder_mail_comes_at_the_lead_time_and_only_once()
    {
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CreateTaskAsync(
            new TaskInput { Title = "Heizungswartung", Kind = TaskKind.HeatingService, DueOn = new DateOnly(2026, 4, 20), IntervalMonths = 12, LeadDays = 14 }, Ct)));
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.SaveSettingsAsync(new DeadlineSettingsInput { DefaultLeadDays = 10, SendReminders = true, SendWeeklySummary = false }, Ct)));

        // The fixed term of the lease (end 31 May, lead time 60 days) is reminded first, from 1 April on.
        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 5, 9, 0, 0, TimeSpan.Zero)); // 15 days before the task
        Assert.Equal(1, await RemindAsync());

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero)); // 14 days before
        Assert.Equal(1, await RemindAsync());
        Assert.Equal(0, await RemindAsync());

        var mails = await WithDbAsync(db => db.Set<OutgoingMail>().Where(m => m.Template == "deadline-reminder").OrderBy(m => m.CreatedAt).ToListAsync(Ct));
        Assert.Equal(2, mails.Count);
        Assert.Equal(["admin@example.de"], mails[1].To);
        Assert.Contains("20.04.2026 (in 14 Tagen): Heizungswartung", mails[1].Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Heizungswartung", mails[0].Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reminders_can_be_switched_off()
    {
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CreateTaskAsync(
            new TaskInput { Title = "Dachrinnen", Kind = TaskKind.Gutters, DueOn = new DateOnly(2026, 3, 2), IntervalMonths = 12 }, Ct)));
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.SaveSettingsAsync(new DeadlineSettingsInput { SendReminders = false, SendWeeklySummary = false }, Ct)));

        Assert.Equal(0, await RemindAsync());
        Assert.Equal(0, await WithAsync(async (DeadlineReminders r, IServiceProvider _) => await r.SendWeeklySummaryAsync(true, Ct)));
    }

    [Fact]
    public async Task Weekly_summary_is_sent_on_Mondays_only()
    {
        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CreateTaskAsync(
            new TaskInput { Title = "Schornsteinfeger", Kind = TaskKind.ChimneySweep, DueOn = new DateOnly(2026, 3, 20), IntervalMonths = 12 }, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 3, 8, 0, 0, TimeSpan.Zero)); // Tuesday
        Assert.Equal(0, await WithAsync(async (DeadlineReminders r, IServiceProvider _) => await r.SendWeeklySummaryAsync(false, Ct)));

        environment.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 9, 8, 0, 0, TimeSpan.Zero)); // Monday
        Assert.True(await WithAsync(async (DeadlineReminders r, IServiceProvider _) => await r.SendWeeklySummaryAsync(false, Ct)) >= 1);

        var mail = await WithDbAsync(db => db.Set<OutgoingMail>().SingleAsync(m => m.Template == "deadline-summary", Ct));
        Assert.Contains("Schornsteinfeger", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calendar_feed_needs_the_secret_link()
    {
        Assert.Null(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarPathAsync(Ct)));
        Assert.Null(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarAsync("irgendwas", Ct)));

        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.RenewCalendarLinkAsync(true, Ct)));
        var path = (await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarPathAsync(Ct)))!;
        var token = path["/kalender/".Length..^".ics".Length];
        Assert.True(token.Length >= 32);

        var feed = await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarAsync(token, Ct));
        Assert.Contains("SUMMARY:Befristung endet: Wohnung 1", feed, StringComparison.Ordinal);
        Assert.Null(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarAsync(token + "x", Ct)));

        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.RenewCalendarLinkAsync(true, Ct)));
        Assert.Null(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarAsync(token, Ct)));

        Validate(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.RenewCalendarLinkAsync(false, Ct)));
        Assert.Null(await WithAsync(async (IDeadlines d, IServiceProvider _) => await d.CalendarPathAsync(Ct)));
    }

    private Task<int> RemindAsync() => WithAsync(async (DeadlineReminders r, IServiceProvider _) => await r.SendRemindersAsync(Ct));

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, IServiceProvider, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>(), scope.ServiceProvider);
    }

    private async Task<TResult> WithDbAsync<TResult>(Func<MietHahnDbContext, Task<TResult>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<MietHahnDbContext>());
    }
}
