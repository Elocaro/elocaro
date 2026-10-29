using Elocaro.Domain.Common;

namespace Elocaro.Domain.Notifications;

/// <summary>A browser or installed app of a user that may be woken up by a push message.</summary>
[NoAudit]
public sealed class PushSubscription : Entity
{
    private PushSubscription()
    {
        Endpoint = "";
        P256dh = "";
        Auth = "";
    }

    public PushSubscription(Guid userId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        UserId = userId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
    }

    public Guid UserId { get; private set; }

    /// <summary>URL of the push service of the browser; the server posts the wake-up call there.</summary>
    public string Endpoint { get; private set; }

    public string P256dh { get; private set; }

    public string Auth { get; private set; }

    public string? UserAgent { get; private set; }

    public void Renew(Guid userId, string p256dh, string auth)
    {
        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
    }
}

/// <summary>
/// A hint for a user. The push message itself carries no content: the service worker fetches the pending hints from the server
/// (<see cref="DeliveredAt"/> is set then), so that title and text never pass through the push service of the browser vendor.
/// </summary>
[NoAudit]
public sealed class PushMessage : Entity
{
    private PushMessage()
    {
        Title = "";
        Body = "";
        Url = "/";
    }

    public PushMessage(Guid userId, string title, string body, string url)
    {
        UserId = userId;
        Title = title;
        Body = body;
        Url = url;
    }

    public Guid UserId { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public string Url { get; private set; }

    /// <summary>The wake-up call was sent to the devices of the user.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>The device fetched the hint.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>The VAPID key pair that identifies this server to the push services (one row).</summary>
[NoAudit]
public sealed class PushKeys : Entity
{
    private PushKeys()
    {
        PublicKey = "";
        ProtectedPrivateKey = "";
    }

    public PushKeys(string publicKey, string protectedPrivateKey)
    {
        PublicKey = publicKey;
        ProtectedPrivateKey = protectedPrivateKey;
    }

    /// <summary>Uncompressed P-256 point, base64url.</summary>
    public string PublicKey { get; private set; }

    /// <summary>PKCS#8 of the private key, protected with data protection.</summary>
    public string ProtectedPrivateKey { get; private set; }
}
