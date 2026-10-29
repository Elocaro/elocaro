using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Accounts;
using Elocaro.Application.Communication;
using Elocaro.Application.Notifications;
using Elocaro.Domain.Notifications;
using Elocaro.Infrastructure.Identity;
using Elocaro.Infrastructure.Notifications;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Notifications;

public class PushNotificationsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/device-1";

    private readonly RecordingHandler handler = new();
    private InfrastructureEnvironment environment = null!;
    private Guid tenant;
    private Guid landlord;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        environment = await InfrastructureEnvironment.CreateAsync(
            postgres,
            null,
            services => services.AddHttpClient(PushNotifications.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
        tenant = await UserAsync("mieter@example.de", Roles.Tenant);
        landlord = await UserAsync("vermieter@example.de", Roles.Admin);
    }

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    private static PushSubscriptionInput Device(string endpoint = Endpoint) => new() { Endpoint = endpoint, P256dh = "BPublicKeyOfTheDevice", Auth = "AuthSecret" };

    [Fact]
    public async Task The_server_key_is_created_once_and_signs_a_verifiable_token()
    {
        var first = await WithAsync(p => p.PublicKeyAsync(Ct));
        var second = await WithAsync(p => p.PublicKeyAsync(Ct));
        Assert.Equal(first, second);
        var point = Convert.FromBase64String(first.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal((65, (byte)4), (point.Length, point[0]));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = PushNotifications.CreateToken(key, "https://fcm.googleapis.com", "mailto:verwaltung@example.de", new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var signature = Convert.FromBase64String(parts[2].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[2].Length % 4) % 4));
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var claims = JsonDocument.Parse(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[1].Length % 4) % 4))).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:verwaltung@example.de", claims.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Only_known_push_services_over_https_are_accepted_and_a_device_is_not_stored_twice()
    {
        Assert.False((await WithAsync(p => p.SubscribeAsync(tenant, Device("http://fcm.googleapis.com/x"), null, Ct))).Succeeded);
        Assert.False((await WithAsync(p => p.SubscribeAsync(tenant, Device("https://intern.example/hook"), null, Ct))).Succeeded);
        Assert.False((await WithAsync(p => p.SubscribeAsync(tenant, Device("https://127.0.0.1/x"), null, Ct))).Succeeded);

        Assert.True((await WithAsync(p => p.SubscribeAsync(tenant, Device(), "Browser", Ct))).Succeeded);
        Assert.True((await WithAsync(p => p.SubscribeAsync(tenant, Device(), "Browser", Ct))).Succeeded);

        Assert.Equal(1, await WithDbAsync(db => db.Set<PushSubscription>().CountAsync(Ct)));
        Assert.True(await WithAsync(p => p.IsSubscribedAsync(tenant, Endpoint, Ct)));
        Assert.False(await WithAsync(p => p.IsSubscribedAsync(landlord, Endpoint, Ct)));
        await WithAsync(p => p.UnsubscribeAsync(tenant, Endpoint, Ct));
        Assert.False(await WithAsync(p => p.IsSubscribedAsync(tenant, Endpoint, Ct)));
    }

    [Fact]
    public async Task Hints_are_queued_only_for_subscribed_users_and_fetched_once()
    {
        await WithAsync(p => p.SubscribeAsync(tenant, Device(), null, Ct));

        await WithAsync(p => p.NotifyAsync([tenant, landlord], "Neue Nachricht", "Es gibt eine neue Nachricht.", "/portal/nachrichten", Ct));

        Assert.Equal(1, await WithDbAsync(db => db.Set<PushMessage>().CountAsync(Ct)));
        var hint = Assert.Single(await WithAsync(p => p.TakePendingAsync(tenant, Ct)));
        Assert.Equal(("Neue Nachricht", "/portal/nachrichten"), (hint.Title, hint.Url));
        Assert.Empty(await WithAsync(p => p.TakePendingAsync(tenant, Ct)));
        Assert.Empty(await WithAsync(p => p.TakePendingAsync(landlord, Ct)));
    }

    [Fact]
    public async Task The_wake_up_call_is_empty_signed_by_the_server_and_a_gone_device_is_removed()
    {
        await WithAsync(p => p.SubscribeAsync(tenant, Device(), null, Ct));
        await WithAsync(p => p.SubscribeAsync(tenant, Device("https://updates.push.services.mozilla.com/wpush/v2/gone"), null, Ct));
        await WithAsync(p => p.NotifyAsync([tenant], "Neue Nachricht", "Text", "/portal/nachrichten", Ct));

        var called = await WithAsync(p => p.DispatchAsync(Ct));

        Assert.Equal(1, called);
        var ok = Assert.Single(handler.Requests, r => r.Uri.Host == "fcm.googleapis.com");
        Assert.StartsWith("vapid t=", ok.Authorization, StringComparison.Ordinal);
        Assert.Contains(", k=", ok.Authorization, StringComparison.Ordinal);
        Assert.Equal(0, ok.ContentLength);
        Assert.Equal(1, await WithDbAsync(db => db.Set<PushSubscription>().CountAsync(Ct)));
        Assert.Equal(0, await WithAsync(p => p.DispatchAsync(Ct))); // sent once
        Assert.NotNull((await WithDbAsync(db => db.Set<PushMessage>().SingleAsync(Ct))).SentAt);
    }

    [Fact]
    public async Task A_notification_mail_also_leaves_a_hint_for_the_recipient_with_the_right_start_page()
    {
        await WithAsync(p => p.SubscribeAsync(tenant, Device(), null, Ct));
        await WithAsync(p => p.SubscribeAsync(landlord, Device("https://fcm.googleapis.com/fcm/send/device-2"), null, Ct));

        await using var scope = environment.Services.CreateAsyncScope();
        var mail = scope.ServiceProvider.GetRequiredService<IMailSender>();
        await mail.EnqueueAsync(new MailRequest(["mieter@example.de"], "message-update", new { Salutation = "Guten Tag,", Message = "Hallo", Link = "https://x/y", SenderName = "A" }), Ct);
        await mail.EnqueueAsync(new MailRequest(["vermieter@example.de"], "message-update", new { Salutation = "Guten Tag,", Message = "Hallo", Link = "https://x/y", SenderName = "A" }), Ct);
        await mail.EnqueueAsync(new MailRequest(["mieter@example.de"], "password-reset", new { DisplayName = "A", Link = "https://x/y" }), Ct);

        var messages = await WithDbAsync(db => db.Set<PushMessage>().ToListAsync(Ct));
        Assert.Equal(2, messages.Count);
        Assert.Equal("/portal/nachrichten", messages.Single(m => m.UserId == tenant).Url);
        Assert.Equal("/verwaltung/nachrichten", messages.Single(m => m.UserId == landlord).Url);
    }

    private async Task<Guid> UserAsync(string email, string role)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private async Task<T> WithAsync<T>(Func<IPushNotifications, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPushNotifications>());
    }

    private async Task WithAsync(Func<IPushNotifications, Task> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IPushNotifications>());
    }

    private async Task<T> WithDbAsync<T>(Func<ElocaroDbContext, Task<T>> action)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ElocaroDbContext>());
    }

    private sealed record Call(Uri Uri, string Authorization, long? ContentLength);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Call> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Call(request.RequestUri!, request.Headers.Authorization?.ToString() ?? string.Join(' ', request.Headers.GetValues("Authorization")), request.Content?.Headers.ContentLength));
            return Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host.Contains("mozilla", StringComparison.Ordinal) ? HttpStatusCode.Gone : HttpStatusCode.Created));
        }
    }
}
