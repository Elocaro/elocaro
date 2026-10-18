using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MietHahn.Integration.Tests.Infrastruktur;

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
    public Dictionary<string, string?> Konfiguration() => new()
    {
        ["Mail:AbsenderAdresse"] = "verwaltung@miethahn.test",
        ["Mail:AbsenderName"] = "Hausverwaltung Test",
        ["Mail:AntwortAn"] = "vermieter@miethahn.test",
        ["Mail:Smtp:Host"] = Host,
        ["Mail:Smtp:Port"] = Smtp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Mail:Smtp:Sicherheit"] = "None",
    };

    /// <summary>Alle zugestellten Nachrichten an die angegebene Adresse.</summary>
    public async Task<IReadOnlyList<MailpitNachricht>> NachrichtenAnAsync(string adresse, CancellationToken cancellationToken)
    {
        var antwort = await http!.GetFromJsonAsync<MailpitSuche>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{adresse}\"")}", cancellationToken);
        var ergebnis = new List<MailpitNachricht>();
        foreach (var kopf in antwort?.Messages ?? [])
        {
            ergebnis.Add((await http!.GetFromJsonAsync<MailpitNachricht>($"/api/v1/message/{kopf.Id}", cancellationToken))!);
        }

        return ergebnis;
    }

    public sealed record MailpitSuche([property: JsonPropertyName("messages")] IReadOnlyList<MailpitKopf> Messages);

    public sealed record MailpitKopf([property: JsonPropertyName("ID")] string Id);

    public sealed record MailpitNachricht(
        [property: JsonPropertyName("Subject")] string Betreff,
        [property: JsonPropertyName("HTML")] string Html,
        [property: JsonPropertyName("Text")] string Text,
        [property: JsonPropertyName("From")] MailpitAdresse Von,
        [property: JsonPropertyName("ReplyTo")] IReadOnlyList<MailpitAdresse> AntwortAn,
        [property: JsonPropertyName("Attachments")] IReadOnlyList<MailpitAnhang> Anhaenge);

    public sealed record MailpitAdresse([property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Address")] string Adresse);

    public sealed record MailpitAnhang([property: JsonPropertyName("FileName")] string Dateiname, [property: JsonPropertyName("ContentType")] string Typ);
}
