using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Elocaro.Application.Common;

namespace Elocaro.Application.Notices;

/// <summary>
/// Direct messages between the tenants of a lease and the administration. The mail only points to the portal
/// (no content, data protection). The tenant sees the threads of the own leases, the administration all.
/// </summary>
public interface IMessages
{
    Task<IReadOnlyList<ThreadRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>The thread with all messages; opening it marks it as read for the caller's side.</summary>
    Task<ThreadView?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tenant: starts a conversation for one of the own current leases. Administration: <see cref="NewThread.LeaseId"/> is required.</summary>
    Task<Result> StartAsync(ClaimsPrincipal user, NewThread input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    Task<Result> ReplyAsync(ClaimsPrincipal user, Guid id, string text, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default);

    /// <summary>Leases the caller can write to (tenant: own current ones).</summary>
    Task<IReadOnlyList<MessageTarget>> TargetsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Administration: all threads of a lease (archive).</summary>
    Task<IReadOnlyList<ThreadView>> ForLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default);
}

public sealed class NewThread
{
    public Guid? LeaseId { get; set; }

    [Required(ErrorMessage = "Bitte einen Betreff angeben.")]
    [StringLength(200)]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "Bitte eine Nachricht schreiben.")]
    [StringLength(4000)]
    public string Text { get; set; } = "";
}

public sealed record MessageTarget(Guid LeaseId, string Unit, string Property, string Tenants);

public sealed record ThreadRow(Guid Id, Guid LeaseId, string Subject, string Unit, string Property, string Tenants, DateTimeOffset LastMessageAt, bool Unread);

public sealed record MessageView(Guid Id, string Author, bool FromTenant, string Text, DateTimeOffset At, IReadOnlyList<Guid> FileIds);

public sealed record ThreadView(ThreadRow Row, IReadOnlyList<MessageView> Messages);
