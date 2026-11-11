using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elocaro.Application.Operations;

namespace Elocaro.Infrastructure.Operations;

/// <summary>
/// Configuration section <c>GoogleDrive</c>: the OAuth client of the application (Google Cloud project, client type "TVs and
/// limited-input devices", scope <c>drive.file</c>). Empty: no sign-in through Google, only the manual token.
/// </summary>
public sealed class GoogleDriveOptions
{
    public const string Section = "GoogleDrive";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }
}

internal sealed class GoogleDriveAuthorization(HttpClient http, IGoogleClientSource client, TimeProvider clock) : IGoogleDriveAuthorization
{
    /// <summary>Only the files the app creates itself (no access to the rest of the Drive).</summary>
    internal const string Scope = "https://www.googleapis.com/auth/drive.file";

    private static readonly Uri DeviceCodeUrl = new("https://oauth2.googleapis.com/device/code");
    private static readonly Uri TokenUrl = new("https://oauth2.googleapis.com/token");

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await client.ResolveAsync(cancellationToken) is { ClientId.Length: > 0, ClientSecret.Length: > 0 };

    public async Task<DeviceAuthorizationStart> StartAsync(CancellationToken cancellationToken = default)
    {
        var (clientId, clientSecret) = await client.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new DeviceAuthorizationStart(null, "Die Anmeldung bei Google ist nicht eingerichtet (GoogleDrive__ClientId und GoogleDrive__ClientSecret).");
        }

        try
        {
            using var response = await http.PostAsync(DeviceCodeUrl, Form(("client_id", clientId), ("scope", Scope)), cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken);
            if (!response.IsSuccessStatusCode || body?.DeviceCode is null || body.UserCode is null)
            {
                return new DeviceAuthorizationStart(null, "Google hat die Anmeldung nicht gestartet" + (string.IsNullOrWhiteSpace(body?.ErrorDescription) ? "." : ": " + body.ErrorDescription));
            }

            return new DeviceAuthorizationStart(new DeviceAuthorization(body.DeviceCode, body.UserCode, body.VerificationUrl ?? "https://www.google.com/device", body.ExpiresIn, Math.Max(body.Interval, 3)), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new DeviceAuthorizationStart(null, "Google ist nicht erreichbar. Besteht vom Server aus eine Internetverbindung?");
        }
    }

    public async Task<DeviceAuthorizationResult> PollAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        var (clientId, clientSecret) = await client.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new DeviceAuthorizationResult(DeviceAuthorizationState.Failed, Message: "Die Anmeldung bei Google ist nicht eingerichtet.");
        }

        try
        {
            using var response = await http.PostAsync(TokenUrl, Form(
                ("client_id", clientId),
                ("client_secret", clientSecret),
                ("device_code", deviceCode),
                ("grant_type", "urn:ietf:params:oauth:grant-type:device_code")), cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (body is null)
            {
                return new DeviceAuthorizationResult(DeviceAuthorizationState.Failed, Message: "Google hat nicht geantwortet.");
            }

            if (response.IsSuccessStatusCode && body.AccessToken is not null)
            {
                // Without a refresh token the backup would stop after an hour.
                return body.RefreshToken is null
                    ? new DeviceAuthorizationResult(DeviceAuthorizationState.Failed, Message: "Google hat keinen dauerhaften Zugang erteilt. Bitte die Anmeldung wiederholen.")
                    : new DeviceAuthorizationResult(DeviceAuthorizationState.Authorized, Credentials: Credentials(body, clientId, clientSecret));
            }

            return body.Error switch
            {
                "authorization_pending" => new DeviceAuthorizationResult(DeviceAuthorizationState.Pending),
                "slow_down" => new DeviceAuthorizationResult(DeviceAuthorizationState.SlowDown),
                "access_denied" => new DeviceAuthorizationResult(DeviceAuthorizationState.Denied, Message: "Der Zugriff wurde bei Google abgelehnt."),
                "expired_token" => new DeviceAuthorizationResult(DeviceAuthorizationState.Expired, Message: "Der Code ist abgelaufen."),
                _ => new DeviceAuthorizationResult(DeviceAuthorizationState.Failed, Message: string.IsNullOrWhiteSpace(body.ErrorDescription) ? "Die Anmeldung bei Google ist fehlgeschlagen." : body.ErrorDescription),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A short hiccup must not end the sign-in: ask again.
            return new DeviceAuthorizationResult(DeviceAuthorizationState.Pending);
        }
    }

    /// <summary>The credentials of the Google Drive target: the token in the format of rclone and the client it belongs to (needed to renew it).</summary>
    private Dictionary<string, string> Credentials(TokenResponse token, string clientId, string clientSecret)
    {
        var expiry = clock.GetUtcNow().AddSeconds(token.ExpiresIn);
        var json = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["access_token"] = token.AccessToken!,
            ["token_type"] = token.TokenType ?? "Bearer",
            ["refresh_token"] = token.RefreshToken!,
            ["expiry"] = expiry.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture),
        });
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BackupProviders.DriveTokenKey] = json,
            [BackupProviders.DriveClientIdKey] = clientId,
            [BackupProviders.DriveClientSecretKey] = clientSecret,
        };
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private sealed record DeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string? DeviceCode,
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("verification_url")] string? VerificationUrl,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
