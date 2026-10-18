namespace MietHahn.Application.Kommunikation;

/// <summary>Reiht E-Mails in die Outbox ein; zugestellt wird asynchron mit Wiederholungen.</summary>
public interface IMailVersand
{
    /// <summary>Rendert die Vorlage, speichert die Mail in der Outbox und liefert ihre Id.</summary>
    Task<Guid> EinreihenAsync(MailAuftrag auftrag, CancellationToken cancellationToken = default);
}

/// <param name="An">Empfängeradressen.</param>
/// <param name="Vorlage">Name der Mailvorlage (ohne Endung), z. B. <c>test</c>.</param>
/// <param name="Modell">Daten für die Vorlage; Eigenschaften sind dort in snake_case verfügbar.</param>
/// <param name="Anhaenge">Ids von Dateien aus der Dateiablage.</param>
/// <param name="Bezug">Optionaler fachlicher Bezug für den Versandnachweis.</param>
public sealed record MailAuftrag(
    IReadOnlyList<string> An,
    string Vorlage,
    object? Modell = null,
    IReadOnlyList<Guid>? Anhaenge = null,
    string? Bezug = null);
