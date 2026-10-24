using System.ComponentModel.DataAnnotations;
using MietHahn.Application.Common;

namespace MietHahn.Application.Notifications;

/// <summary>
/// Web push for the installed web app: hints for new messages, tickets, deadlines and documents without opening the page.
/// The push message is empty; the title and text of the hint are fetched by the service worker from the server.
/// </summary>
public interface IPushNotifications
{
    /// <summary>The public VAPID key (base64url) the browser needs to subscribe.</summary>
    Task<string> PublicKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the subscription of a device; the same device is renewed, not duplicated.</summary>
    Task<Result> SubscribeAsync(Guid userId, PushSubscriptionInput input, string? userAgent, CancellationToken cancellationToken = default);

    Task<Result> UnsubscribeAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default);

    Task<bool> IsSubscribedAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Queues a hint for the users that have at least one device subscribed; others are skipped.</summary>
    Task NotifyAsync(IEnumerable<Guid> userIds, string title, string body, string url, CancellationToken cancellationToken = default);

    /// <summary>The hints the user has not fetched yet (marked as fetched).</summary>
    Task<IReadOnlyList<PushHint>> TakePendingAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sends the wake-up calls for queued hints; returns the number of devices called.</summary>
    Task<int> DispatchAsync(CancellationToken cancellationToken = default);
}

public sealed class PushSubscriptionInput
{
    [Required]
    [StringLength(1000)]
    public string? Endpoint { get; set; }

    [Required]
    [StringLength(200)]
    public string? P256dh { get; set; }

    [Required]
    [StringLength(100)]
    public string? Auth { get; set; }
}

public sealed record PushHint(string Title, string Body, string Url);
