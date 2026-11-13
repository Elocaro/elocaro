using Elocaro.Application.Receipts;
using Elocaro.Infrastructure.Receipts;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Elocaro.Integration.Tests.Receipts;

public class ReceiptInboxTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        environment = await InfrastructureEnvironment.CreateAsync(postgres, new Dictionary<string, string?>
        {
            ["ReceiptInbox:AllowedSenders:0"] = "verwalter@example.de",
            ["ReceiptInbox:AllowedSenders:1"] = "@stadtwerke.example",
            ["ReceiptInbox:MaxAttachmentMegabytes"] = "1",
        });

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static MimeMessage Mail(string from, string subject, params (string Name, byte[] Content)[] attachments)
    {
        var message = new MimeMessage { Subject = subject, Date = new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero) };
        message.From.Add(new MailboxAddress("Stadtwerke", from));
        var body = new BodyBuilder { TextBody = "Anbei die Rechnung." };
        foreach (var (name, content) in attachments)
        {
            body.Attachments.Add(name, content);
        }

        message.Body = body.ToMessageBody();
        return message;
    }

    private async Task<InboxOutcome> ProcessAsync(MimeMessage message)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReceiptInbox>().ProcessAsync(message, Ct);
    }

    [Fact]
    public async Task Attachments_of_an_allowed_sender_become_one_open_receipt()
    {
        var outcome = await ProcessAsync(Mail("rechnung@stadtwerke.example", "Rechnung 2026-17", ("rechnung.pdf", "%PDF-1.4 test"u8.ToArray()), ("agb.docx", [1, 2, 3])));

        Assert.True(outcome.Created, outcome.Reason);
        await using var scope = environment.Services.CreateAsyncScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptService>().GetAsync(outcome.ReceiptId!.Value, Ct))!;
        Assert.Equal(new DateOnly(2026, 3, 5), receipt.Data.InvoiceDate);
        Assert.Equal(0m, receipt.Data.Gross);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IReceiptService>().SearchAsync(new ReceiptQuery(), Ct), r => r.Incomplete);
        Assert.Single(receipt.Files);
        Assert.Contains("Rechnung 2026-17", receipt.Data.Note, StringComparison.Ordinal);
        Assert.Contains("agb.docx", receipt.Data.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_zugferd_attachment_fills_the_receipt_even_without_a_provider()
    {
        var outcome = await ProcessAsync(Mail("rechnung@stadtwerke.example", "Rechnung", ("rechnung.pdf", EInvoiceTests.PdfWithAttachment("factur-x.xml", EInvoiceTests.Cii))));

        Assert.True(outcome.Created, outcome.Reason);
        await using var scope = environment.Services.CreateAsyncScope();
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptService>().GetAsync(outcome.ReceiptId!.Value, Ct))!;
        Assert.Equal("Mustermann Haustechnik GmbH", receipt.Data.SupplierName);
        Assert.Equal("RE-2026-0815", receipt.Data.InvoiceNumber);
        Assert.Equal(297.50m, receipt.Data.Gross);
        Assert.Contains("E-Rechnung", receipt.Data.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IReceiptService>().SearchAsync(new ReceiptQuery(), Ct), r => r.Incomplete);
    }

    [Fact]
    public async Task Entering_the_amount_completes_the_receipt()
    {
        var outcome = await ProcessAsync(Mail("rechnung@stadtwerke.example", "Rechnung", ("rechnung.pdf", "%PDF-1.4 test"u8.ToArray())));
        await using var scope = environment.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReceiptService>();
        var data = (await service.GetAsync(outcome.ReceiptId!.Value, Ct))!.Data;

        Assert.True((await service.UpdateAsync(outcome.ReceiptId.Value, data, Ct)).Succeeded); // other details can be saved without the amount
        Assert.Single(await service.SearchAsync(new ReceiptQuery(), Ct), r => r.Incomplete);

        data.Gross = 119m;
        Assert.True((await service.UpdateAsync(outcome.ReceiptId.Value, data, Ct)).Succeeded);
        Assert.DoesNotContain(await service.SearchAsync(new ReceiptQuery(), Ct), r => r.Incomplete);
    }

    [Fact]
    public async Task Unknown_senders_mails_without_usable_attachment_and_oversized_files_are_rejected()
    {
        var stranger = await ProcessAsync(Mail("fremd@example.org", "Rechnung", ("rechnung.pdf", "%PDF-1.4 test"u8.ToArray())));
        var none = await ProcessAsync(Mail("verwalter@example.de", "Nur Text"));
        var big = await ProcessAsync(Mail("verwalter@example.de", "Groß", ("gross.pdf", [.. "%PDF-1.4 "u8.ToArray(), .. new byte[2 * 1024 * 1024]])));

        Assert.False(stranger.Created);
        Assert.Equal("Absender nicht freigegeben", stranger.Reason);
        Assert.False(none.Created);
        Assert.False(big.Created);
        Assert.Contains("MB", big.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("verwalter@example.de", true)]
    [InlineData("VERWALTER@example.de", true)]
    [InlineData("x@stadtwerke.example", true)]
    [InlineData("x@evil.stadtwerke.example.org", false)]
    [InlineData("x@example.de", false)]
    public void Allowed_senders_are_addresses_or_domains(string address, bool expected) =>
        Assert.Equal(expected, ReceiptInbox.IsAllowed(address, ["verwalter@example.de", "@stadtwerke.example"]));

    [Fact]
    public async Task Without_configuration_polling_does_nothing()
    {
        await using var scope = environment.Services.CreateAsyncScope();

        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ReceiptInbox>().PollAsync(Ct));
    }
}
