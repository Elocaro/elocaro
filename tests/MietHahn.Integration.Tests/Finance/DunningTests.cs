using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Accounts;
using MietHahn.Application.Common;
using MietHahn.Application.Files;
using MietHahn.Application.Finance;
using MietHahn.Application.Leases;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Todos;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Communication;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Files;
using MietHahn.Domain.Finance;
using MietHahn.Infrastructure.Jobs;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Integration.Tests.Infrastructure;
using Quartz;

namespace MietHahn.Integration.Tests.Finance;

public class DunningTests(PostgresFixture postgres, GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>, IAsyncLifetime
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "test"));

    private static readonly JobKey DunningJobKey = new("dunning", "finance");

    private InfrastructureEnvironment environment = null!;
    private Guid lease;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Pdf:GotenbergUrl"] = gotenberg.Url });
        await using var scope = environment.Services.CreateAsyncScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
        var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct));
        var apartment = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct));
        var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { FirstName = "Anna", LastName = "Schmidt", Salutation = MietHahn.Domain.People.Salutation.Mrs, Email = "anna@example.de" }, null, Ct));
        lease = Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation
        {
            RentalUnitId = apartment,
            Start = new DateOnly(2026, 3, 1),
            TenantIds = [anna],
            BaseRent = 620m,
            OperatingCostPrepayment = 150m,
        }, Ct));

        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        db.Add(new LandlordProfile("Andy Hahn", "Musterweg 1", "12345", "Musterstadt") { Email = "vermieter@example.de", Iban = "DE02 1203 0000 0000 2020 51", Bank = "Musterbank" });
        await db.SaveChangesAsync(Ct);
        await SaveSettingsAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 3, 1) });
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Arrears_lead_to_a_proposal_but_nothing_is_created_or_sent_without_approval()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();

        Assert.Equal(1, await WithAsync(d => d.RefreshAsync(null, Ct)));
        Assert.Equal(0, await WithAsync(d => d.RefreshAsync(null, Ct))); // idempotent

        var overview = await WithAsync(d => d.OverviewAsync(Ct));
        var proposal = Assert.Single(overview.Proposals);
        Assert.Equal(DunningLevel.Reminder, proposal.Level);
        Assert.Equal(DunningStatus.Proposed, proposal.Status);
        Assert.Equal(770m, proposal.Principal);
        Assert.Equal("Anna Schmidt", proposal.Tenant);
        Assert.Empty(overview.Issued);

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Empty(await db.Set<OutgoingMail>().ToListAsync(Ct));
        Assert.Empty(await db.Set<StoredFile>().ToListAsync(Ct));
        Assert.Empty(await db.Set<Document>().ToListAsync(Ct));
        Assert.DoesNotContain(await db.Set<Charge>().ToListAsync(Ct), c => c.Kind == ChargeType.DunningFee);
    }

    [Fact]
    public async Task Dashboard_shows_the_proposal_and_requires_the_finance_permission()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();

        var todos = await TodosAsync(Admin);

        var todo = Assert.Single(todos, t => t.Title.Contains("Mahnvorschlag", StringComparison.Ordinal));
        Assert.Equal("1 Mahnvorschlag prüfen", todo.Title);
        Assert.Equal("/verwaltung/finanzen/mahnungen", todo.Link);
        Assert.Empty(await TodosAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Manager)], "test"))));
    }

    [Fact]
    public async Task Payment_after_the_proposal_but_before_approval_removes_the_proposal()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();

        Validate(await WithAsync<ITenantAccounts, Result>(a => a.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 3, 20), Amount = 770m }, Ct)));
        var result = await WithAsync(d => d.IssueAsync(proposal.Id, new DunningIssueInput { SendByMail = true }, Ct));

        Assert.False(result.Succeeded);
        Assert.Contains("Der Rückstand besteht nicht mehr", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Null(await WithAsync(d => d.GetAsync(proposal.Id, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Empty(await db.Set<OutgoingMail>().ToListAsync(Ct));
        Assert.Empty(await db.Set<Document>().ToListAsync(Ct));
    }

    [Fact]
    public async Task Refresh_removes_a_proposal_when_the_arrears_are_paid()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        await WithAsync(d => d.RefreshAsync(null, Ct));

        Validate(await WithAsync<ITenantAccounts, Result>(a => a.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 3, 20), Amount = 770m }, Ct)));

        Assert.Equal(1, await WithAsync(d => d.RefreshAsync(null, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);
    }

    [Fact]
    public async Task Approval_stores_letter_and_document_books_the_fee_and_sends_the_mail()
    {
        await SaveSettingsAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 3, 1), ReminderFee = 2.50m });
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        var details = (await WithAsync(d => d.GetAsync(proposal.Id, Ct)))!;
        Assert.Equal(["anna@example.de"], details.Recipients);
        Assert.Equal(["Anna Schmidt", "Wohnung 1", "Lindenweg 3", "50667 Köln"], details.Address); // no personal address: the rented property
        Assert.Equal(new DateOnly(2026, 3, 27), details.Deadline);

        var preview = await WithAsync(d => d.PreviewAsync(proposal.Id, new DunningIssueInput(), Ct));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(preview, 0, 4), StringComparison.Ordinal);
        Assert.Equal(DunningStatus.Proposed, (await WithAsync(d => d.GetAsync(proposal.Id, Ct)))!.Row.Status); // preview changes nothing

        var result = await WithAsync(d => d.IssueAsync(proposal.Id, new DunningIssueInput { SendByMail = true }, Ct));

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        var issued = Assert.Single((await WithAsync(d => d.OverviewAsync(Ct))).Issued);
        Assert.Equal(DunningLevel.Reminder, issued.Level);
        Assert.Equal(new DateOnly(2026, 3, 20), issued.Date);
        Assert.Equal(new DateOnly(2026, 3, 27), issued.Deadline);
        Assert.True(issued.SentByMail);
        Assert.Equal(2.50m, issued.Fee);

        await using (var opened = await WithAsync<IFileStore, OpenedFile?>(f => f.OpenAsync(issued.FileId!.Value, Ct)))
        {
            Assert.Equal("application/pdf", opened!.StoredFile.MimeType);
            Assert.Equal("Zahlungserinnerung 2026-03-20.pdf", opened.StoredFile.Name);
        }

        var text = await PdfTextAsync(issued.FileId!.Value);
        Assert.Contains("Sehr geehrte Frau Schmidt,", text, StringComparison.Ordinal);
        Assert.Contains("bis zum 27.03.2026", text, StringComparison.Ordinal);
        Assert.Contains("Kaltmiete 03/2026", text, StringComparison.Ordinal);
        Assert.Contains("2,50 €", text, StringComparison.Ordinal);
        Assert.Contains("772,50 €", text, StringComparison.Ordinal);
        Assert.Contains("DE02 1203 0000 0000 2020 51", text, StringComparison.Ordinal);
        Assert.DoesNotContain("§ 288", text, StringComparison.Ordinal); // no interest in the reminder

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var document = await db.Set<Document>().SingleAsync(Ct);
        Assert.Equal(DocumentCategory.Correspondence, document.Category);
        Assert.Equal(lease, document.LeaseId);
        Assert.Equal(issued.FileId, document.FileId);
        Assert.False(document.SharedWithTenants);
        var mail = await db.Set<OutgoingMail>().SingleAsync(Ct);
        Assert.Equal(["anna@example.de"], mail.To);
        Assert.Equal("Zahlungserinnerung zu Ihrer Mietzahlung", mail.Subject);
        Assert.Equal([issued.FileId!.Value], mail.AttachmentFileIds);
        Assert.Equal($"dunning:{proposal.Id}", mail.Reference);
        Assert.Contains("Sehr geehrte Frau Schmidt,", mail.Html, StringComparison.Ordinal);
        var fee = await db.Set<Charge>().SingleAsync(c => c.Kind == ChargeType.DunningFee, Ct);
        Assert.Equal(2.50m, fee.Amount);
        Assert.Equal(new DateOnly(2026, 3, 27), fee.Due);

        // Same day: the next level waits for the waiting period.
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);
        Assert.False((await WithAsync(d => d.IssueAsync(proposal.Id, new DunningIssueInput(), Ct))).Succeeded); // already decided
    }

    [Fact]
    public async Task Levels_escalate_with_the_waiting_periods_and_interest_is_booked_only_once()
    {
        await SaveSettingsAsync(new FinanceSettingsInput
        {
            AccountingStart = new DateOnly(2026, 3, 1),
            FirstNoticeFee = 5m,
            ChargeDefaultInterest = true,
            BaseRate = 2.27m,
            BaseRateAsOf = new DateOnly(2026, 3, 1),
        });
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        await IssueAsync(sendByMail: false);

        Now(2026, 4, 2); // 13 days later: too early
        await GenerateChargesAsync();
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);

        Now(2026, 4, 3);
        var due = (await WithAsync<ITenantAccounts, TenantAccount?>(a => a.GetAsync(lease, Ct)))!.Entries.First(e => e.Automatic).Date;
        var first = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        Assert.Equal(DunningLevel.FirstNotice, first.Level);
        Assert.Equal(5m, first.Fee);
        var interest1 = DefaultInterest.Calculate(620m, due, new DateOnly(2026, 4, 3), 7.27m) + DefaultInterest.Calculate(150m, due, new DateOnly(2026, 4, 3), 7.27m);
        Assert.Equal(interest1, first.Interest);
        Assert.True(interest1 > 0);

        // Without the checkboxes neither fee nor interest is demanded or booked.
        var issued = await IssueAsync(first.Id, new DunningIssueInput { IncludeFee = false, IncludeInterest = false });
        Assert.Equal(0m, issued.Fee);
        Assert.Equal(0m, issued.Interest);
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            Assert.DoesNotContain(await db.Set<Charge>().ToListAsync(Ct), c => c.Kind is ChargeType.DefaultInterest or ChargeType.DunningFee);
        }

        Now(2026, 4, 17);
        await GenerateChargesAsync();
        var second = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        Assert.Equal(DunningLevel.SecondNotice, second.Level);
        Assert.Equal([DunningLevel.FirstNotice, DunningLevel.Reminder], (await WithAsync(d => d.OverviewAsync(Ct))).Issued.Select(m => m.Level));

        var booked = await IssueAsync(second.Id, new DunningIssueInput());
        Assert.True(booked.Interest > 0);
        var text = await PdfTextAsync(booked.FileId!.Value);
        Assert.Contains("2. Mahnung", text, StringComparison.Ordinal);
        Assert.Contains("letztmalig", text, StringComparison.Ordinal);
        Assert.Contains("§ 288 Abs. 1 BGB", text, StringComparison.Ordinal);
        Assert.Contains("7,27 % p. a.", text, StringComparison.Ordinal);
        Assert.Contains("Basiszinssatz 2,27 %", text, StringComparison.Ordinal);
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var charge = await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<Charge>().SingleAsync(c => c.Kind == ChargeType.DefaultInterest, Ct);
            Assert.Equal(booked.Interest, charge.Amount);
            Assert.Equal($"Verzugszinsen bis 17.04.2026", charge.Name);
        }

        // Nothing follows the second notice.
        Now(2026, 8, 1);
        await GenerateChargesAsync();
        Assert.DoesNotContain((await WithAsync(d => d.OverviewAsync(Ct))).Proposals, m => m.Level == DunningLevel.SecondNotice);
    }

    [Fact]
    public async Task Hold_suppresses_proposals_and_removes_an_open_one()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        Assert.Single((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);

        var hold = Validate(await WithAsync(d => d.AddHoldAsync(lease, new DunningHoldInput { Reason = "Ratenvereinbarung bis Juni" }, Ct)));

        var overview = await WithAsync(d => d.OverviewAsync(Ct));
        Assert.Empty(overview.Proposals);
        Assert.Equal("Ratenvereinbarung bis Juni", Assert.Single(overview.Holds).Reason);
        Assert.Single((await WithAsync(d => d.ForLeaseAsync(lease, Ct))).Holds);

        Validate(await WithAsync(d => d.EndHoldAsync(hold, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Holds);
        Assert.Single((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);
    }

    [Fact]
    public async Task Hold_with_an_end_date_expires()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        Validate(await WithAsync(d => d.AddHoldAsync(lease, new DunningHoldInput { Reason = "Stundung", Until = new DateOnly(2026, 3, 31) }, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);

        Now(2026, 4, 1);
        await GenerateChargesAsync();

        var overview = await WithAsync(d => d.OverviewAsync(Ct));
        Assert.Empty(overview.Holds);
        Assert.Single(overview.Proposals);
        Assert.False((await WithAsync(d => d.AddHoldAsync(lease, new DunningHoldInput { Reason = "Zu spät", Until = new DateOnly(2026, 3, 1) }, Ct))).Succeeded);
        Assert.False((await WithAsync(d => d.AddHoldAsync(lease, new DunningHoldInput { Reason = " " }, Ct))).Succeeded);
    }

    [Fact]
    public async Task Dismissed_proposal_returns_only_after_14_days()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();

        Validate(await WithAsync(d => d.DismissAsync(proposal.Id, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);

        Now(2026, 4, 3);
        await GenerateChargesAsync();
        Assert.Single((await WithAsync(d => d.OverviewAsync(Ct))).Proposals);
    }

    [Fact]
    public async Task Mail_approval_needs_an_address_and_a_landlord_profile()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var person = await db.Set<MietHahn.Domain.People.Person>().SingleAsync(Ct);
            person.Email = null;
            await db.SaveChangesAsync(Ct);
        }

        var rejected = await WithAsync(d => d.IssueAsync(proposal.Id, new DunningIssueInput { SendByMail = true }, Ct));
        Assert.False(rejected.Succeeded);
        Assert.Contains("keine E-Mail-Adresse", rejected.Errors.Single(), StringComparison.Ordinal);

        Assert.True((await WithAsync(d => d.IssueAsync(proposal.Id, new DunningIssueInput { SendByMail = false }, Ct))).Succeeded);
        await using var check = environment.Services.CreateAsyncScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<OutgoingMail>().ToListAsync(Ct));
    }

    [Fact]
    public async Task Termination_relevance_is_only_a_hint_and_shown_on_the_dashboard()
    {
        Now(2026, 5, 20); // March to May unpaid: 3 x 770 €
        await GenerateChargesAsync();

        var overview = await WithAsync(d => d.OverviewAsync(Ct));

        var hint = Assert.Single(overview.TerminationHints);
        Assert.Equal(lease, hint.LeaseId);
        Assert.Equal(770m, hint.MonthlyRent);
        Assert.Equal(2310m, hint.OpenRent);
        Assert.True(hint.Relevance.TwoMonthlyRents);
        Assert.True(hint.Relevance.TwoConsecutiveTerms);
        Assert.True(overview.Proposals.Single().TerminationRelevant);
        var todo = Assert.Single(await TodosAsync(Admin), t => t.Title.StartsWith("Rückstand mit Kündigungsrelevanz", StringComparison.Ordinal));
        Assert.Equal(Urgency.Urgent, todo.Urgency);
        Assert.Contains("nur ein Hinweis", todo.Details, StringComparison.Ordinal);
        Assert.Equal(lease, (await WithAsync(d => d.ForLeaseAsync(lease, Ct))).TerminationHint!.LeaseId);

        // Paying two rents ends the relevance (only March remains).
        Validate(await WithAsync<ITenantAccounts, Result>(a => a.RecordPaymentAsync(lease, new PaymentInput { Date = new DateOnly(2026, 5, 20), Amount = 1540m }, Ct)));
        Assert.Empty((await WithAsync(d => d.OverviewAsync(Ct))).TerminationHints);
    }

    [Fact]
    public async Task Job_proposes_but_sends_nothing()
    {
        await using var jobs = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?> { ["Jobs:Active"] = "true" });
        await using (var scope = jobs.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var anna = Validate(await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(new PersonInput { LastName = "Test", Email = "test@example.de" }, null, Ct));
            var properties = scope.ServiceProvider.GetRequiredService<IPropertyService>();
            var house = Validate(await properties.CreateAsync(new PropertyInput { Name = "Haus", Street = "Weg 1", PostalCode = "50667", City = "Köln" }, Ct));
            var unit = Validate(await properties.CreateUnitAsync(house, new UnitInput { Name = "W1" }, Ct));
            Validate(await scope.ServiceProvider.GetRequiredService<ILeaseService>().CreateAsync(new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 3, 1), TenantIds = [anna], BaseRent = 500m }, Ct));
            await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(new FinanceSettingsInput { AccountingStart = new DateOnly(2026, 3, 1) }, Ct);
            jobs.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));
            await scope.ServiceProvider.GetRequiredService<ITenantAccounts>().GenerateChargesAsync(null, Ct);
            Assert.Empty(await db.Set<MietHahn.Domain.Finance.Dunning>().ToListAsync(Ct));
        }

        var scheduler = await jobs.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);
        await scheduler.Start(Ct);
        try
        {
            Assert.True(await scheduler.CheckExists(DunningJobKey, Ct));
            await scheduler.TriggerJob(DunningJobKey, Ct);
            var timeout = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < timeout)
            {
                await using var scope = jobs.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
                if (await db.Set<JobRun>().AnyAsync(r => r.JobName == "dunning" && r.EndedAt != null, Ct))
                {
                    break;
                }

                await Task.Delay(200, Ct);
            }

            await using var check = jobs.Services.CreateAsyncScope();
            var checkDb = check.ServiceProvider.GetRequiredService<MietHahnDbContext>();
            var proposal = await checkDb.Set<MietHahn.Domain.Finance.Dunning>().SingleAsync(Ct);
            Assert.Equal(DunningStatus.Proposed, proposal.Status);
            Assert.Empty(await checkDb.Set<OutgoingMail>().ToListAsync(Ct));
            Assert.Empty(await checkDb.Set<StoredFile>().ToListAsync(Ct));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Issued_dunnings_appear_in_the_change_log_and_the_GDPR_report()
    {
        Now(2026, 3, 20);
        await GenerateChargesAsync();
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        await IssueAsync(proposal.Id, new DunningIssueInput());

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var actions = await db.Set<MietHahn.Infrastructure.Audit.AuditEntry>().Where(a => a.EntityType == "Dunning" && a.EntityId == proposal.Id).Select(a => a.Action).ToListAsync(Ct);
        Assert.Contains(MietHahn.Application.Audit.AuditAction.Created, actions);
        Assert.Contains(MietHahn.Application.Audit.AuditAction.Modified, actions);

        var personId = await db.Set<MietHahn.Domain.Leases.LeaseParty>().Select(p => p.PersonId).SingleAsync(Ct);
        var source = scope.ServiceProvider.GetServices<MietHahn.Application.People.IPersonalDataSource>().Single(s => s.Section == "dunnings");
        Assert.NotNull(await source.DataAsync(personId, Ct));
    }

    private void Now(int year, int month, int day) => environment.Clock.SetUtcNow(new DateTimeOffset(year, month, day, 10, 0, 0, TimeSpan.Zero));

    private async Task SaveSettingsAsync(FinanceSettingsInput input)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFinanceSettings>().SaveAsync(input, Ct);
    }

    private async Task GenerateChargesAsync() => await WithAsync<ITenantAccounts, int>(a => a.GenerateChargesAsync(null, Ct));

    private async Task<DunningRow> IssueAsync(bool sendByMail)
    {
        var proposal = (await WithAsync(d => d.OverviewAsync(Ct))).Proposals.Single();
        return await IssueAsync(proposal.Id, new DunningIssueInput { SendByMail = sendByMail });
    }

    private async Task<DunningRow> IssueAsync(Guid id, DunningIssueInput options)
    {
        Validate(await WithAsync(d => d.IssueAsync(id, options, Ct)));
        return (await WithAsync(d => d.OverviewAsync(Ct))).Issued.Single(m => m.Id == id);
    }

    private async Task<string> PdfTextAsync(Guid fileId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await using var opened = (await scope.ServiceProvider.GetRequiredService<IFileStore>().OpenAsync(fileId, Ct))!;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, Ct);
        using var document = UglyToad.PdfPig.PdfDocument.Open(buffer.ToArray());
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<ITodoSource>().Single(q => q.GetType().Name == "DunningTodos");
        return await source.TodosAsync(user, Ct);
    }

    private static Guid Validate(Result result)
    {
        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        return result.Id!.Value;
    }

    private Task<T> WithAsync<T>(Func<IDunning, Task<T>> action) => WithAsync<IDunning, T>(action);

    private async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
