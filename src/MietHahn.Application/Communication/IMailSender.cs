namespace MietHahn.Application.Communication;

/// <summary>Reiht E-Mails in die Outbox ein; zugestellt wird asynchron mit Wiederholungen.</summary>
public interface IMailSender
{
    /// <summary>Rendert die Vorlage, speichert die Mail in der Outbox und liefert ihre Id.</summary>
    Task<Guid> EnqueueAsync(MailRequest job, CancellationToken cancellationToken = default);
}

/// <param name="To">Empfängeradressen.</param>
/// <param name="Template">Name der Mailvorlage (ohne Endung), z. B. <c>test</c>.</param>
/// <param name="Model">Daten für die Vorlage; Eigenschaften sind dort in snake_case verfügbar.</param>
/// <param name="Attachments">Ids von Dateien aus der Dateiablage.</param>
/// <param name="Reference">Optionaler fachlicher Bezug für den Versandnachweis.</param>
public sealed record MailRequest(
    IReadOnlyList<string> To,
    string Template,
    object? Model = null,
    IReadOnlyList<Guid>? Attachments = null,
    string? Reference = null);
