using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Communication;
using Elocaro.Application.People;
using Elocaro.Application.Privacy;
using Elocaro.Application.Todos;
using Elocaro.Domain.Common;
using Elocaro.Domain.Communication;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Operations;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elocaro.Integration.Tests.Mail;

public class MailContentProtectionTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres, mailpit.Configuration());

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Sign_in_link_is_kept_for_delivery_and_removed_afterwards_with_the_proof_remaining()
    {
        var recipient = $"mieter-{Guid.NewGuid():N}@example.de";
        await InviteTenantAsync(recipient);
        var waiting = await MailToAsync(recipient);
        Assert.Contains("/konto/link/", waiting.Html, StringComparison.Ordinal);
        Assert.Contains("/konto/link/", waiting.Text, StringComparison.Ordinal);
        Assert.Null(waiting.ContentPurgedAt);

        await DeliverAsync(environment);

        Assert.Single(await mailpit.MessagesToAsync(recipient, Ct));
        var sent = await MailToAsync(recipient);
        Assert.Equal(MailStatus.Sent, sent.Status);
        Assert.Equal(OutgoingMail.PurgedContent, sent.Html);
        Assert.Equal(OutgoingMail.PurgedContent, sent.Text);
        Assert.Equal(InfrastructureEnvironment.StartTime, sent.ContentPurgedAt);
        Assert.Equal([recipient], sent.To);
        Assert.Equal(waiting.Subject, sent.Subject);
        Assert.Equal(InfrastructureEnvironment.StartTime, sent.SentAt);
    }

    [Fact]
    public async Task Ordinary_mail_keeps_its_content_after_delivery()
    {
        var recipient = $"mieter-{Guid.NewGuid():N}@example.de";
        await EnqueueTestMailAsync(recipient);

        await DeliverAsync(environment);

        var sent = await MailToAsync(recipient);
        Assert.Equal(MailStatus.Sent, sent.Status);
        Assert.Contains("Testnachricht", sent.Text, StringComparison.Ordinal);
        Assert.Null(sent.ContentPurgedAt);
    }

    [Fact]
    public async Task Failed_mail_with_a_link_is_cleared_and_cannot_be_resent_but_is_reported_for_a_week()
    {
        var configuration = mailpit.Configuration();
        configuration["Mail:Smtp:Port"] = "1"; // nobody is listening there
        await using var withoutServer = await InfrastructureEnvironment.CreateAsync(postgres, configuration);
        await using (var scope = withoutServer.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountService>().InviteAsync("mieter@example.de", "Mara", Roles.Tenant, Ct);
        }

        for (var i = 0; i < OutgoingMail.MaxAttempts; i++)
        {
            await DeliverAsync(withoutServer);
            withoutServer.Clock.Advance(TimeSpan.FromHours(7));
        }

        await using var check = withoutServer.Services.CreateAsyncScope();
        var mail = await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal(MailStatus.Failed, mail.Status);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Text);
        Assert.DoesNotContain("konto/link", mail.Html, StringComparison.Ordinal);

        var service = check.ServiceProvider.GetRequiredService<IMailService>();
        Assert.True((await service.LatestAsync(10, Ct)).Single().ContentPurged);
        await Assert.ThrowsAsync<RuleViolationException>(() => service.RetryAsync(mail.Id, Ct));
        var stored = await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal(MailStatus.Failed, stored.Status);

        var admin = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Admin)], "Test"));
        var todos = check.ServiceProvider.GetServices<ITodoSource>().OfType<MailTodos>().Single();
        Assert.Single(await todos.TodosAsync(admin, Ct));
        withoutServer.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Empty(await todos.TodosAsync(admin, Ct));
    }

    [Fact]
    public void Secret_templates_exist()
    {
        var resources = typeof(MailOutbox).Assembly.GetManifestResourceNames();

        Assert.All(OutgoingMail.SecretTemplates, t => Assert.Contains(resources, r => r.EndsWith($"Mail.Templates.{t}.sbn", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Retention_job_deletes_expired_mails_only()
    {
        await EnqueueTestMailAsync("alt@example.de");
        await DeliverAsync(environment);
        await EnqueueTestMailAsync("wartet@example.de");
        await SetStatusAsync("wartet@example.de", "Pending"); // due far in the future, so it stays in the outbox

        environment.Clock.Advance(TimeSpan.FromDays(1096));
        await EnqueueTestMailAsync("neu@example.de");
        await DeliverAsync(environment);

        await RunJobAsync(environment);

        Assert.Null(await FindMailToAsync("alt@example.de"));
        Assert.NotNull(await FindMailToAsync("neu@example.de"));
        Assert.NotNull(await FindMailToAsync("wartet@example.de"));
    }

    [Fact]
    public async Task Retention_can_be_switched_off()
    {
        await EnqueueTestMailAsync("alt@example.de");
        await DeliverAsync(environment);
        environment.Clock.Advance(TimeSpan.FromDays(4000));

        await RunJobAsync(environment, retentionDays: 0);

        Assert.NotNull(await FindMailToAsync("alt@example.de"));
    }

    [Fact]
    public async Task Job_clears_already_delivered_secret_mails_but_not_waiting_ones()
    {
        await InviteTenantAsync("alt@example.de");
        await SetStatusAsync("alt@example.de", "Sent");
        await InviteTenantAsync("wartet@example.de");

        await RunJobAsync(environment);

        Assert.Equal(OutgoingMail.PurgedContent, (await MailToAsync("alt@example.de")).Html);
        Assert.Contains("/konto/link/", (await MailToAsync("wartet@example.de")).Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_clears_stored_secret_mails_and_nothing_else()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElocaroDbContext>();
        var migrator = db.GetService<IMigrator>();
        var migrations = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_OutgoingMailContentPurge", StringComparison.Ordinal));
        await migrator.MigrateAsync(migrations[index - 1], Ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO outgoing_mails (id, "to", subject, html, text, attachment_file_ids, template, status, attempts, next_attempt_at, created_at, deleted) VALUES
            ('00000000-0000-7000-8000-000000000001', ARRAY['a@example.de'], 'Einladung', 'geheim', 'geheim', ARRAY[]::uuid[], 'invitation-link', 'Sent', 1, now(), now(), false),
            ('00000000-0000-7000-8000-000000000002', ARRAY['b@example.de'], 'Signatur', 'geheim', 'geheim', ARRAY[]::uuid[], 'signature-request', 'Failed', 8, now(), now(), false),
            ('00000000-0000-7000-8000-000000000003', ARRAY['c@example.de'], 'Wartet', 'geheim', 'geheim', ARRAY[]::uuid[], 'magic-link', 'Pending', 0, now(), now(), false),
            ('00000000-0000-7000-8000-000000000004', ARRAY['d@example.de'], 'Abrechnung', 'bleibt', 'bleibt', ARRAY[]::uuid[], 'statement', 'Sent', 1, now(), now(), false)
            """, Ct);

        await migrator.MigrateAsync(migrations[index], Ct);

        var mails = await db.Set<OutgoingMail>().AsNoTracking().OrderBy(m => m.Id).ToListAsync(Ct);
        Assert.Equal([OutgoingMail.PurgedContent, OutgoingMail.PurgedContent, "geheim", "bleibt"], mails.Select(m => m.Text));
        Assert.Equal([OutgoingMail.PurgedContent, OutgoingMail.PurgedContent, "geheim", "bleibt"], mails.Select(m => m.Html));
        Assert.Equal([true, true, false, false], mails.Select(m => m.ContentPurgedAt is not null));
    }

    [Fact]
    public async Task Anonymizing_a_person_clears_their_mails_and_the_access_report_shows_no_secret()
    {
        Guid person;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var people = scope.ServiceProvider.GetRequiredService<IPersonService>();
            person = (await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "Anna@Example.de" }, null, Ct)).Id!.Value;
        }

        await EnqueueTestMailAsync("anna@example.de", "bert@example.de");
        await EnqueueTestMailAsync("bert@example.de");
        await InviteTenantAsync("anna@example.de");
        await DeliverAsync(environment);
        await EnqueueTestMailAsync("anna@example.de");

        var report = await AccessReportAsync(person);
        var listed = report.GetProperty("mails").EnumerateArray().ToList();
        Assert.Equal(3, listed.Count);
        Assert.Contains(listed, m => m.GetProperty("text").GetString()!.Contains("Testnachricht", StringComparison.Ordinal));
        Assert.DoesNotContain("/konto/link/", report.ToString(), StringComparison.Ordinal);

        await using (var scope = environment.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IPrivacyService>().AnonymizeAsync(person, Ct);
            Assert.True(result.Succeeded);
        }

        await using var check = environment.Services.CreateAsyncScope();
        var mails = await check.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().AsNoTracking().ToListAsync(Ct);
        Assert.Equal(4, mails.Count);
        Assert.DoesNotContain(mails, m => m.To.Contains("anna@example.de", StringComparer.OrdinalIgnoreCase));

        var anonymized = mails.Where(m => m.To.Contains(OutgoingMail.AnonymizedAddress)).ToList();
        Assert.Equal(3, anonymized.Count);
        Assert.All(anonymized, m =>
        {
            Assert.Equal(OutgoingMail.AnonymizedSubject, m.Subject);
            Assert.Equal(OutgoingMail.PurgedContent, m.Text);
            Assert.NotEqual(MailStatus.Pending, m.Status);
        });
        Assert.Contains(anonymized, m => m.To.Contains("bert@example.de"));

        var untouched = mails.Single(m => !m.To.Contains(OutgoingMail.AnonymizedAddress));
        Assert.Contains("Testnachricht", untouched.Text, StringComparison.Ordinal);
    }

    private static async Task DeliverAsync(InfrastructureEnvironment target)
    {
        await using var scope = target.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MailOutbox>().DeliverAsync(Ct);
    }

    private static async Task RunJobAsync(InfrastructureEnvironment target, int retentionDays = 1095)
    {
        await using var scope = target.Services.CreateAsyncScope();
        var retention = new MailRetention(
            scope.ServiceProvider.GetRequiredService<ElocaroDbContext>(), target.Clock, Options.Create(new MailOptions { RetentionDays = retentionDays }));
        await retention.CleanUpAsync(Ct);
    }

    private async Task InviteTenantAsync(string recipient)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAccountService>().InviteAsync(recipient, "Mara Mieterin", Roles.Tenant, Ct);
        Assert.True(result.Succeeded);
    }

    private async Task EnqueueTestMailAsync(params string[] recipients)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMailSender>().EnqueueAsync(
            new MailRequest(recipients, "test", new { SentAt = new DateOnly(2026, 3, 1), TriggeredBy = "admin" }), Ct);
    }

    private async Task<OutgoingMail> MailToAsync(string recipient) =>
        await FindMailToAsync(recipient) ?? throw new InvalidOperationException($"Keine Mail an {recipient}.");

    private async Task<OutgoingMail?> FindMailToAsync(string recipient)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().AsNoTracking().ToListAsync(Ct))
            .SingleOrDefault(m => m.To.Contains(recipient));
    }

    private async Task SetStatusAsync(string recipient, string status)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var parsed = Enum.Parse<MailStatus>(status);
        var id = (await MailToAsync(recipient)).Id;
        await scope.ServiceProvider.GetRequiredService<ElocaroDbContext>().Set<OutgoingMail>().Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, parsed).SetProperty(m => m.NextAttemptAt, DateTimeOffset.MaxValue), Ct);
    }

    private async Task<JsonElement> AccessReportAsync(Guid personId)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var bytes = await scope.ServiceProvider.GetRequiredService<IPrivacyService>().AccessReportAsync(personId, Ct);
        return JsonDocument.Parse(bytes!).RootElement;
    }
}
