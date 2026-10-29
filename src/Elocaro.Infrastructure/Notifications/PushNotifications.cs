using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elocaro.Application.Common;
using Elocaro.Application.Notifications;
using Elocaro.Domain.Common;
using Elocaro.Domain.Notifications;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Mail;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Notifications;

/// <summary>Configuration section <c>Push</c>.</summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    /// <summary>Contact for the push services (<c>mailto:</c> or URL); defaults to the sender address of the mails.</summary>
    public string? Contact { get; set; }

    /// <summary>Further hosts of push services besides those of Google, Mozilla, Apple and Microsoft.</summary>
    public string[] AllowedHosts { get; set; } = [];
}

internal sealed class PushNotifications(
    ElocaroDbContext db,
    IHttpClientFactory httpFactory,
    IDataProtectionProvider protection,
    IOptions<PushOptions> options,
    IOptions<MailOptions> mail,
    TimeProvider clock,
    ILogger<PushNotifications> logger) : IPushNotifications
{
    public const string ClientName = "push";

    private static readonly string[] KnownHosts =
    [
        "fcm.googleapis.com", "android.googleapis.com", "updates.push.services.mozilla.com", "push.services.mozilla.com",
        "push.apple.com", "notify.windows.com",
    ];

    public async Task<string> PublicKeyAsync(CancellationToken cancellationToken = default) => (await KeysAsync(cancellationToken)).PublicKey;

    public Task<Result> SubscribeAsync(Guid userId, PushSubscriptionInput input, string? userAgent, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            var endpoint = input.Endpoint?.Trim() ?? "";
            if (!IsAllowedEndpoint(endpoint))
            {
                throw new RuleViolationException("Dieser Push-Dienst wird nicht unterstützt.");
            }

            if (string.IsNullOrWhiteSpace(input.P256dh) || string.IsNullOrWhiteSpace(input.Auth))
            {
                throw new RuleViolationException("Die Angaben des Geräts sind unvollständig.");
            }

            var existing = await db.Set<PushSubscription>().FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);
            if (existing is null)
            {
                existing = new PushSubscription(userId, endpoint, input.P256dh, input.Auth, userAgent);
                db.Add(existing);
            }
            else
            {
                existing.Renew(userId, input.P256dh, input.Auth);
            }

            await db.SaveChangesAsync(cancellationToken);
            return existing.Id;
        });

    public Task<Result> UnsubscribeAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var subscription = await db.Set<PushSubscription>().FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint, cancellationToken);
            if (subscription is not null)
            {
                db.Remove(subscription);
                await db.SaveChangesAsync(cancellationToken);
            }

            return userId;
        });

    public async Task<bool> IsSubscribedAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default) =>
        await db.Set<PushSubscription>().AnyAsync(s => s.UserId == userId && s.Endpoint == endpoint, cancellationToken);

    public async Task NotifyAsync(IEnumerable<Guid> userIds, string title, string body, string url, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var subscribed = await db.Set<PushSubscription>().AsNoTracking().Where(s => ids.Contains(s.UserId)).Select(s => s.UserId).Distinct().ToListAsync(cancellationToken);
        foreach (var userId in subscribed)
        {
            db.Add(new PushMessage(userId, title, body, url));
        }

        if (subscribed.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PushHint>> TakePendingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var messages = await db.Set<PushMessage>().Where(m => m.UserId == userId && m.DeliveredAt == null).OrderBy(m => m.CreatedAt).Take(10).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var message in messages)
        {
            message.DeliveredAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return messages.Select(m => new PushHint(m.Title, m.Body, m.Url)).ToList();
    }

    public async Task<int> DispatchAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var pending = await db.Set<PushMessage>().Where(m => m.SentAt == null && m.DeliveredAt == null).ToListAsync(cancellationToken);
        var called = 0;
        if (pending.Count > 0)
        {
            var keys = await KeysAsync(cancellationToken);
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(protection.CreateProtector("push-vapid").Unprotect(keys.ProtectedPrivateKey)), out _);
            var client = httpFactory.CreateClient(ClientName);
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var userId in pending.Select(m => m.UserId).Distinct())
            {
                foreach (var subscription in await db.Set<PushSubscription>().Where(s => s.UserId == userId).ToListAsync(cancellationToken))
                {
                    called += await SendAsync(client, subscription, key, keys.PublicKey, tokens, now, cancellationToken) ? 1 : 0;
                }
            }

            foreach (var message in pending)
            {
                message.SentAt = now;
            }
        }

        // Fetched hints are only kept for a while.
        var cutoff = now.AddDays(-14);
        db.RemoveRange(await db.Set<PushMessage>().Where(m => m.CreatedAt < cutoff && (m.DeliveredAt != null || m.SentAt != null)).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return called;
    }

    /// <summary>JWT for the VAPID header (RFC 8292): audience is the origin of the push service.</summary>
    internal static string CreateToken(ECDsa key, string audience, string subject, DateTimeOffset expires)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["aud"] = audience, ["exp"] = expires.ToUnixTimeSeconds(), ["sub"] = subject }));
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal bool IsAllowedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || endpoint.Length > 1000)
        {
            return false;
        }

        return KnownHosts.Concat(options.Value.AllowedHosts).Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> SendAsync(HttpClient client, PushSubscription subscription, ECDsa key, string publicKey, Dictionary<string, string> tokens, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var uri = new Uri(subscription.Endpoint);
        var audience = uri.GetLeftPart(UriPartial.Authority);
        if (!tokens.TryGetValue(audience, out var token))
        {
            var contact = options.Value.Contact ?? $"mailto:{mail.Value.SenderAddress}";
            tokens[audience] = token = CreateToken(key, audience, contact, now.AddHours(12));
        }

        // An empty push message: nothing readable passes through the push service.
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent([]) };
        request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={token}, k={publicKey}");
        request.Headers.TryAddWithoutValidation("TTL", "86400");
        request.Headers.TryAddWithoutValidation("Urgency", "normal");
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                db.Remove(subscription);
            }
            else
            {
                PushLog.Rejected(logger, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            PushLog.NotReachable(logger, uri.Host);
        }

        return false;
    }

    private async Task<PushKeys> KeysAsync(CancellationToken cancellationToken)
    {
        var keys = await db.Set<PushKeys>().OrderBy(k => k.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (keys is not null)
        {
            if (CanRead(keys))
            {
                return keys;
            }

            // Keys written under another data-protection key ring (e.g. after restoring a backup into a renamed installation)
            // are lost for good; the browsers' subscriptions belong to the old public key and are useless as well.
            PushLog.KeysUnreadable(logger);
            db.RemoveRange(await db.Set<PushKeys>().ToListAsync(cancellationToken));
            db.RemoveRange(await db.Set<PushSubscription>().ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 4;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);
        keys = new PushKeys(Base64Url(point), protection.CreateProtector("push-vapid").Protect(Convert.ToBase64String(key.ExportPkcs8PrivateKey())));
        db.Add(keys);
        await db.SaveChangesAsync(cancellationToken);
        return keys;
    }

    private bool CanRead(PushKeys keys)
    {
        try
        {
            protection.CreateProtector("push-vapid").Unprotect(keys.ProtectedPrivateKey);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

internal static partial class PushLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "A push service answered with status {Status}.")]
    public static partial void Rejected(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The push service {Host} is not reachable.")]
    public static partial void NotReachable(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored push keys cannot be read any more; new keys are created and existing subscriptions are discarded.")]
    public static partial void KeysUnreadable(ILogger logger);
}
