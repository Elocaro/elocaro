using Elocaro.Domain.Common;
using Elocaro.Domain.Communication;

namespace Elocaro.Domain.Tests.Communication;

public class OutgoingMailTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static OutgoingMail NewMail(params string[] to) =>
        new(to.Length == 0 ? ["mieter@example.de"] : to, "Betreff", "<p>Hallo</p>", "Hallo", Now);

    [Fact]
    public void New_mail_is_due_immediately()
    {
        var mail = NewMail();

        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal(Now, mail.NextAttemptAt);
        Assert.Equal(0, mail.Attempts);
    }

    [Fact]
    public void Recipients_are_sanitized_and_deduplicated()
    {
        var mail = NewMail(" a@example.de ", "A@example.de", "", "b@example.de");

        Assert.Equal(["a@example.de", "b@example.de"], mail.To);
    }

    [Fact]
    public void Missing_recipient_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => NewMail(" "));
    }

    [Fact]
    public void Invalid_address_is_rejected()
    {
        var errors = Assert.Throws<ArgumentException>(() => NewMail("kein-at-zeichen"));
        Assert.Contains("kein-at-zeichen", errors.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(9, 256)]
    [InlineData(10, 360)]
    [InlineData(30, 360)]
    public void Delay_grows_exponentially_up_to_six_hours(int failedAttempts, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), OutgoingMail.RetryDelay(failedAttempts));
    }

    [Fact]
    public void Failed_attempt_schedules_next_attempt_with_backoff()
    {
        var mail = NewMail();

        mail.MarkFailedAttempt(Now, "SMTP nicht erreichbar");
        mail.MarkFailedAttempt(Now.AddMinutes(1), "SMTP nicht erreichbar");

        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal(2, mail.Attempts);
        Assert.Equal(Now.AddMinutes(3), mail.NextAttemptAt);
        Assert.Equal("SMTP nicht erreichbar", mail.LastError);
    }

    [Fact]
    public void After_max_attempts_the_mail_has_failed()
    {
        var mail = NewMail();

        for (var i = 0; i < OutgoingMail.MaxAttempts; i++)
        {
            mail.MarkFailedAttempt(Now, "Fehler");
        }

        Assert.Equal(MailStatus.Failed, mail.Status);
        Assert.Equal(OutgoingMail.MaxAttempts, mail.Attempts);
    }

    [Fact]
    public void Long_error_text_is_truncated()
    {
        var mail = NewMail();

        mail.MarkFailedAttempt(Now, new string('x', 5000));

        Assert.Equal(OutgoingMail.MaxErrorLength, mail.LastError!.Length);
    }

    [Fact]
    public void Successful_sending_is_recorded()
    {
        var mail = NewMail();
        mail.MarkFailedAttempt(Now, "Fehler");

        mail.MarkSent(Now.AddMinutes(1));

        Assert.Equal(MailStatus.Sent, mail.Status);
        Assert.Equal(Now.AddMinutes(1), mail.SentAt);
        Assert.Null(mail.LastError);
    }

    [Fact]
    public void Failed_mail_can_be_retried()
    {
        var mail = NewMail();
        for (var i = 0; i < OutgoingMail.MaxAttempts; i++)
        {
            mail.MarkFailedAttempt(Now, "Fehler");
        }

        mail.Retry(Now.AddDays(1));

        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal(0, mail.Attempts);
        Assert.Equal(Now.AddDays(1), mail.NextAttemptAt);
    }

    [Fact]
    public void Sent_mail_cannot_be_retried()
    {
        var mail = NewMail();
        mail.MarkSent(Now);

        Assert.Throws<InvalidOperationException>(() => mail.Retry(Now));
    }

    private static OutgoingMail SecretMail(params string[] to) =>
        new(to.Length == 0 ? ["mieter@example.de"] : to, "Ihr Anmeldelink", "<a href=\"https://x/konto/link/geheim\">Anmelden</a>", "https://x/konto/link/geheim", Now, template: "magic-link");

    [Fact]
    public void Secret_mail_keeps_its_content_while_retrying_and_loses_it_when_sent()
    {
        var mail = SecretMail();

        mail.MarkFailedAttempt(Now, "kein Server");
        Assert.Contains("geheim", mail.Text, StringComparison.Ordinal);

        mail.MarkSent(Now);

        Assert.Equal(OutgoingMail.PurgedContent, mail.Html);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Text);
        Assert.Equal(Now, mail.ContentPurgedAt);
        Assert.Equal("Ihr Anmeldelink", mail.Subject);
        Assert.Equal(["mieter@example.de"], mail.To);
    }

    [Fact]
    public void Secret_mail_loses_its_content_on_final_failure_and_cannot_be_retried()
    {
        var mail = SecretMail();

        for (var i = 0; i < OutgoingMail.MaxAttempts; i++)
        {
            mail.MarkFailedAttempt(Now, "kein Server");
        }

        Assert.Equal(MailStatus.Failed, mail.Status);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Text);
        Assert.Throws<RuleViolationException>(() => mail.Retry(Now));
        Assert.Equal(MailStatus.Failed, mail.Status);
    }

    [Fact]
    public void Ordinary_mail_keeps_its_content_and_can_be_retried_after_final_failure()
    {
        var mail = NewMail();

        for (var i = 0; i < OutgoingMail.MaxAttempts; i++)
        {
            mail.MarkFailedAttempt(Now, "kein Server");
        }

        mail.Retry(Now);

        Assert.Equal(MailStatus.Pending, mail.Status);
        Assert.Equal("Hallo", mail.Text);
        Assert.Null(mail.ContentPurgedAt);
    }

    [Fact]
    public void Waiting_mail_cannot_be_purged()
    {
        Assert.Throws<InvalidOperationException>(() => NewMail().PurgeContent(Now));
    }

    [Fact]
    public void Anonymizing_replaces_address_subject_and_content_and_stops_a_waiting_mail()
    {
        var mail = NewMail("Anna@example.de", "bert@example.de");
        mail.MarkFailedAttempt(Now, "550 Anna@example.de unbekannt");

        mail.AnonymizeRecipients(["anna@example.de"], Now);

        Assert.Equal([OutgoingMail.AnonymizedAddress, "bert@example.de"], mail.To);
        Assert.Equal(OutgoingMail.AnonymizedSubject, mail.Subject);
        Assert.Equal(OutgoingMail.PurgedContent, mail.Text);
        Assert.Null(mail.LastError);
        Assert.Equal(MailStatus.Failed, mail.Status);
        Assert.Throws<RuleViolationException>(() => mail.Retry(Now));
    }
}
