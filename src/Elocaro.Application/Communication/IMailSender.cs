namespace Elocaro.Application.Communication;

/// <summary>Enqueues emails in the outbox; delivery happens asynchronously with retries.</summary>
public interface IMailSender
{
    /// <summary>Renders the template, stores the mail in the outbox and returns its id.</summary>
    Task<Guid> EnqueueAsync(MailRequest job, CancellationToken cancellationToken = default);
}

/// <param name="To">Recipient addresses.</param>
/// <param name="Template">Name of the mail template (without extension), e.g. <c>test</c>.</param>
/// <param name="Model">Data for the template; properties are available there in snake_case.</param>
/// <param name="Attachments">Ids of files from the file store.</param>
/// <param name="Reference">Optional business reference for the proof of delivery.</param>
public sealed record MailRequest(
    IReadOnlyList<string> To,
    string Template,
    object? Model = null,
    IReadOnlyList<Guid>? Attachments = null,
    string? Reference = null);
