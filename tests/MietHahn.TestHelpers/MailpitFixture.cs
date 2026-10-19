using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MietHahn.TestHelpers;

/// <summary>Mailpit als echter SMTP-Server mit HTTP-API zum Prüfen zugestellter Mails.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer container = new ContainerBuilder("axllent/mailpit:v1.31.6")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(ApiPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/livez")))
        .Build();

    private HttpClient? http;

    public string Host => container.Hostname;

    public int Smtp => container.GetMappedPublicPort(SmtpPort);

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        http = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(ApiPort)}") };
    }

    public async ValueTask DisposeAsync()
    {
        http?.Dispose();
        await container.DisposeAsync();
    }

    /// <summary>Konfigurationswerte für <c>Mail:*</c>, die auf diesen Mailpit zeigen.</summary>
    public Dictionary<string, string?> Configuration() => new()
    {
        ["Mail:SenderAddress"] = "verwaltung@miethahn.test",
        ["Mail:SenderName"] = "Hausverwaltung Test",
        ["Mail:ReplyTo"] = "vermieter@miethahn.test",
        ["Mail:Smtp:Host"] = Host,
        ["Mail:Smtp:Port"] = Smtp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Mail:Smtp:Security"] = "None",
    };

    /// <summary>Alle zugestellten Nachrichten an die angegebene Adresse.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(string address, CancellationToken cancellationToken)
    {
        var response = await http!.GetFromJsonAsync<MailpitSearch>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", cancellationToken);
        var result = new List<MailpitMessage>();
        foreach (var header in response?.Messages ?? [])
        {
            result.Add((await http!.GetFromJsonAsync<MailpitMessage>($"/api/v1/message/{header.Id}", cancellationToken))!);
        }

        return result;
    }

    public sealed record MailpitSearch([property: JsonPropertyName("messages")] IReadOnlyList<MailpitHeader> Messages);

    public sealed record MailpitHeader([property: JsonPropertyName("ID")] string Id);

    public sealed record MailpitMessage(
        [property: JsonPropertyName("Subject")] string Subject,
        [property: JsonPropertyName("HTML")] string Html,
        [property: JsonPropertyName("Text")] string Text,
        [property: JsonPropertyName("From")] MailpitAddress From,
        [property: JsonPropertyName("ReplyTo")] IReadOnlyList<MailpitAddress> ReplyTo,
        [property: JsonPropertyName("Attachments")] IReadOnlyList<MailpitAttachment> Attachments);

    public sealed record MailpitAddress([property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Address")] string Address);

    public sealed record MailpitAttachment([property: JsonPropertyName("FileName")] string FileName, [property: JsonPropertyName("ContentType")] string Type);
}
