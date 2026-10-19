using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Communication;
using MietHahn.Application.Files;
using MietHahn.Domain.Communication;
using MietHahn.Infrastructure.Mail;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Templates;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Mail;

public class MailSenderTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres, mailpit.Configuration());

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public async Task Queued_mail_is_rendered_and_stored_in_the_layout()
    {
        var id = await EnqueueAsync(new MailRequest(["mieter@example.de"], "test", new { SentAt = new DateOnly(2026, 3, 1), TriggeredBy = "<admin>" }, Reference: "Test:1"));

        var mail = await MailAsync(id);
        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal("Testnachricht von MietHahn", mail.Subject);
        Assert.Equal("test", mail.Template);
        Assert.Equal("Test:1", mail.Reference);
        Assert.Contains("<!DOCTYPE html>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Hausverwaltung Test", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;admin&gt;", mail.Html, StringComparison.Ordinal);
        Assert.Contains("dies ist eine Testnachricht", mail.Text, StringComparison.Ordinal);
        Assert.Contains("<admin>", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", mail.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../geheim")]
    [InlineData("Test")]
    [InlineData("")]
    public async Task Invalid_template_name_is_rejected(string template)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => EnqueueAsync(new MailRequest(["a@example.de"], template)));
    }

    [Fact]
    public async Task Missing_model_for_template_is_reported()
    {
        await Assert.ThrowsAsync<TemplateErrorException>(() => EnqueueAsync(new MailRequest(["a@example.de"], "test")));
    }

    [Fact]
    public async Task Outbox_delivers_mail_with_attachment_via_SMTP()
    {
        var recipients = $"mieter-{Guid.NewGuid():N}@example.de";
        Guid attachmentId;
        await using (var scope = environment.Services.CreateAsyncScope())
        {
            using var pdf = new MemoryStream(TestFiles.Pdf("Abrechnung"));
            attachmentId = (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(pdf, "Abrechnung 2025.pdf", Ct)).Id;
        }

        var id = await EnqueueAsync(new MailRequest([recipients], "test", TestModel(), [attachmentId]));
        var sent = await DeliverAsync();

        Assert.Equal(1, sent);
        var mail = await MailAsync(id);
        Assert.Equal(MailStatus.Sent, mail.Status);
        Assert.Equal(InfrastructureEnvironment.StartTime, mail.SentAt);

        var delivered = Assert.Single(await mailpit.MessagesToAsync(recipients, Ct));
        Assert.Equal("Testnachricht von MietHahn", delivered.Subject);
        Assert.Equal("verwaltung@miethahn.test", delivered.From.Address);
        Assert.Equal("Hausverwaltung Test", delivered.From.Name);
        Assert.Equal("vermieter@miethahn.test", Assert.Single(delivered.ReplyTo).Address);
        Assert.Contains("dies ist eine Testnachricht", delivered.Html, StringComparison.Ordinal);
        Assert.Contains("dies ist eine Testnachricht", delivered.Text, StringComparison.Ordinal);
        var attachment = Assert.Single(delivered.Attachments);
        Assert.Equal("Abrechnung 2025.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.Type);
    }

    [Fact]
    public async Task Already_sent_mail_is_not_delivered_again()
    {
        var recipients = $"einmal-{Guid.NewGuid():N}@example.de";
        await EnqueueAsync(new MailRequest([recipients], "test", TestModel()));

        Assert.Equal(1, await DeliverAsync());
        Assert.Equal(0, await DeliverAsync());
        Assert.Single(await mailpit.MessagesToAsync(recipients, Ct));
    }

    [Fact]
    public async Task Unreachable_server_is_retried_with_backoff()
    {
        var configuration = mailpit.Configuration();
        configuration["Mail:Smtp:Port"] = "1"; // nobody is listening there
        await using var withoutServer = await InfrastructureEnvironment.CreateAsync(postgres, configuration);

        Guid id;
        await using (var scope = withoutServer.Services.CreateAsyncScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IMailSender>().EnqueueAsync(new MailRequest(["a@example.de"], "test", TestModel()), Ct);
        }

        Assert.Equal(0, await DeliverAsync(withoutServer));
        var mail = await MailAsync(id, withoutServer);
        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal(1, mail.Attempts);
        Assert.NotNull(mail.LastError);
        Assert.Equal(InfrastructureEnvironment.StartTime.AddMinutes(1), mail.NextAttemptAt);

        // Nothing is attempted before the wait time has elapsed; afterwards it retries.
        withoutServer.Clock.Advance(TimeSpan.FromSeconds(30));
        await DeliverAsync(withoutServer);
        Assert.Equal(1, (await MailAsync(id, withoutServer)).Attempts);

        withoutServer.Clock.Advance(TimeSpan.FromSeconds(31));
        await DeliverAsync(withoutServer);
        Assert.Equal(2, (await MailAsync(id, withoutServer)).Attempts);
    }

    [Fact]
    public void Text_version_is_derived_from_HTML()
    {
        var text = MailSender.HtmlToText("<h1>Titel</h1><p>Hallo &amp; willkommen,<br>zweite Zeile</p><p><a href=\"https://miethahn.test/x\">Link</a></p>");

        Assert.Equal("Titel\nHallo & willkommen,\nzweite Zeile\nLink (https://miethahn.test/x)", text);
    }

    private static object TestModel() => new { SentAt = new DateOnly(2026, 3, 1), TriggeredBy = "admin" };

    private async Task<Guid> EnqueueAsync(MailRequest job)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMailSender>().EnqueueAsync(job, Ct);
    }

    private Task<int> DeliverAsync() => DeliverAsync(environment);

    private static async Task<int> DeliverAsync(InfrastructureEnvironment u)
    {
        await using var scope = u.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MailOutbox>().DeliverAsync(Ct);
    }

    private Task<OutgoingMail> MailAsync(Guid id) => MailAsync(id, environment);

    private static async Task<OutgoingMail> MailAsync(Guid id, InfrastructureEnvironment u)
    {
        await using var scope = u.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<OutgoingMail>().AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }
}
