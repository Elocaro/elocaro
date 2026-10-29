using System.Net;
using System.Text.Json;
using Elocaro.Application.Receipts;
using Microsoft.Extensions.Logging;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>Shared HTTP handling of the providers: error mapping to user messages without leaking secrets or content.</summary>
internal static class ExtractionHttp
{
    public static readonly string[] ImageTypes = ["image/jpeg", "image/png"];

    public static async Task<JsonDocument> SendAsync(HttpClient http, HttpRequestMessage request, string provider, ILogger logger, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, limit.Token);
        }
        catch (HttpRequestException ex)
        {
            // Only the exception type and status are logged, never request data (documents, key).
            ExtractionLog.NotReachable(logger, provider, ex.HttpRequestError);
            throw new ExtractionException($"{provider} ist nicht erreichbar. Bitte Verbindung und Adresse in der Konfiguration prüfen.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            ExtractionLog.TimedOut(logger, provider);
            throw new ExtractionException($"{provider} hat nicht rechtzeitig geantwortet. Bitte später erneut versuchen.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ExtractionLog.Rejected(logger, provider, (int)response.StatusCode);
                throw new ExtractionException(StatusMessage(provider, response.StatusCode));
            }

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(limit.Token);
                return await JsonDocument.ParseAsync(body, cancellationToken: limit.Token);
            }
            catch (JsonException ex)
            {
                throw new ExtractionException($"Die Antwort von {provider} war nicht lesbar.", ex);
            }
        }
    }

    private static string StatusMessage(string provider, HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{provider} lehnt den Zugang ab. Bitte den API-Schlüssel in der Konfiguration prüfen.",
        HttpStatusCode.NotFound => $"{provider}: Modell oder Adresse nicht gefunden. Bitte Modellname und Adresse in der Konfiguration prüfen.",
        HttpStatusCode.RequestEntityTooLarge => "Die Datei ist für den Dienst zu groß. Bitte weniger oder kleinere Seiten verwenden.",
        HttpStatusCode.TooManyRequests => $"{provider} ist ausgelastet oder das Kontingent ist erschöpft. Bitte später erneut versuchen.",
        _ when (int)status >= 500 => $"{provider} meldet einen Fehler ({(int)status}). Bitte später erneut versuchen.",
        _ => $"{provider} hat die Anfrage abgelehnt ({(int)status}).",
    };
}
