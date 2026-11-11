using System.Net;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Operations;
using Elocaro.Infrastructure.Operations;
using Microsoft.Extensions.Time.Testing;

namespace Elocaro.Integration.Tests.Operations;

public sealed class GoogleDriveAuthorizationTests : IDisposable
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly Handler handler = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => handler.Dispose();

    private GoogleDriveAuthorization Service(string? clientId = "client-id", string? clientSecret = "client-secret") =>
        new(new HttpClient(handler), new FixedClient(clientId, clientSecret), clock);

    [Fact]
    public async Task Without_a_configured_client_nothing_is_sent_to_google()
    {
        var service = Service(clientId: null);

        Assert.False(await service.IsAvailableAsync(Ct));
        var start = await service.StartAsync(Ct);
        Assert.Null(start.Authorization);
        Assert.Contains("nicht eingerichtet", start.Error, StringComparison.Ordinal);
        Assert.Equal(DeviceAuthorizationState.Failed, (await service.PollAsync("code", Ct)).State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Start_asks_for_the_narrow_drive_scope_and_returns_what_the_person_has_to_do()
    {
        handler.Answer = _ => Json(HttpStatusCode.OK, """{"device_code":"dev-123","user_code":"ABC-DEF-GHIJ","expires_in":1800,"interval":5,"verification_url":"https://www.google.com/device"}""");

        var start = await Service().StartAsync(Ct);

        var authorization = start.Authorization!;
        Assert.Equal("ABC-DEF-GHIJ", authorization.UserCode);
        Assert.Equal("https://www.google.com/device", authorization.VerificationUrl);
        Assert.Equal(5, authorization.IntervalSeconds);
        Assert.Equal(1800, authorization.ExpiresInSeconds);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://oauth2.googleapis.com/device/code", request.Url);
        Assert.Contains("client_id=client-id", request.Body, StringComparison.Ordinal);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_reports_a_refusal_and_an_unreachable_google()
    {
        handler.Answer = _ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"The OAuth client was not found."}""");
        var refused = await Service().StartAsync(Ct);
        Assert.Null(refused.Authorization);
        Assert.Contains("The OAuth client was not found.", refused.Error, StringComparison.Ordinal);

        handler.Answer = _ => throw new HttpRequestException("no route");
        var offline = await Service().StartAsync(Ct);
        Assert.Null(offline.Authorization);
        Assert.Contains("nicht erreichbar", offline.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("authorization_pending", DeviceAuthorizationState.Pending)]
    [InlineData("slow_down", DeviceAuthorizationState.SlowDown)]
    [InlineData("access_denied", DeviceAuthorizationState.Denied)]
    [InlineData("expired_token", DeviceAuthorizationState.Expired)]
    [InlineData("invalid_grant", DeviceAuthorizationState.Failed)]
    public async Task Poll_maps_the_answers_of_google(string error, DeviceAuthorizationState expected)
    {
        handler.Answer = _ => Json(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}"}""");

        var result = await Service().PollAsync("dev-123", Ct);

        Assert.Equal(expected, result.State);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Url);
        Assert.Contains("device_code=dev-123", request.Body, StringComparison.Ordinal);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", request.Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=client-secret", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_confirmed_sign_in_becomes_the_credentials_of_a_google_drive_target()
    {
        handler.Answer = _ => Json(HttpStatusCode.OK, """{"access_token":"at-1","refresh_token":"rt-1","token_type":"Bearer","expires_in":3599,"scope":"https://www.googleapis.com/auth/drive.file"}""");

        var result = await Service().PollAsync("dev-123", Ct);

        Assert.Equal(DeviceAuthorizationState.Authorized, result.State);
        var credentials = result.Credentials!;
        Assert.Equal("client-id", credentials[BackupProviders.DriveClientIdKey]);
        Assert.Equal("client-secret", credentials[BackupProviders.DriveClientSecretKey]);
        // The token in the format of rclone, with expiry
        using var token = JsonDocument.Parse(credentials[BackupProviders.DriveTokenKey]);
        Assert.Equal("at-1", token.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("rt-1", token.RootElement.GetProperty("refresh_token").GetString());
        Assert.Equal("Bearer", token.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(clock.GetUtcNow().AddSeconds(3599), DateTimeOffset.Parse(token.RootElement.GetProperty("expiry").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        // They pass the validation of the provider
        Assert.Empty(BackupProviders.Validate(Elocaro.Domain.Operations.BackupProvider.GoogleDrive, "Elocaro-Sicherung", credentials, "pw"));
    }

    [Fact]
    public async Task Without_a_refresh_token_the_sign_in_does_not_count()
    {
        handler.Answer = _ => Json(HttpStatusCode.OK, """{"access_token":"at-1","token_type":"Bearer","expires_in":3599}""");

        var result = await Service().PollAsync("dev-123", Ct);

        Assert.Equal(DeviceAuthorizationState.Failed, result.State);
        Assert.Null(result.Credentials);
    }

    [Fact]
    public async Task A_short_network_hiccup_while_waiting_does_not_end_the_sign_in()
    {
        handler.Answer = _ => throw new HttpRequestException("reset");

        Assert.Equal(DeviceAuthorizationState.Pending, (await Service().PollAsync("dev-123", Ct)).State);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FixedClient(string? id, string? secret) : IGoogleClientSource
    {
        public Task<(string? ClientId, string? ClientSecret)> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult((id, secret));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Answer(request);
        }
    }
}
