using MietHahn.Application.Gemeinsam;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Domain.Personen;

namespace MietHahn.Application.Personen;

/// <summary>Pflege von Personen: Mieter, Bürgen und Kontakte (Hausmeister, Handwerker, Versorger …).</summary>
public interface IPersonenVerwaltung
{
    Task<IReadOnlyList<PersonUebersicht>> SucheAsync(PersonenSuche suche, CancellationToken cancellationToken = default);

    /// <summary>Lädt die Personenakte und protokolliert den Zugriff (DSGVO).</summary>
    Task<PersonDetails?> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Ergebnis> AnlegenAsync(PersonDaten daten, AnschriftEingabe? anschrift = null, CancellationToken cancellationToken = default);

    Task<Ergebnis> AendernAsync(Guid id, PersonDaten daten, CancellationToken cancellationToken = default);

    Task<Ergebnis> AnschriftSetzenAsync(Guid id, AnschriftEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> BankverbindungHinzufuegenAsync(Guid id, BankverbindungEingabe eingabe, CancellationToken cancellationToken = default);

    Task<Ergebnis> BankverbindungEntfernenAsync(Guid id, Guid bankverbindungId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verknüpft die Person mit einem Benutzerkonto: ein vorhandenes Konto mit derselben E-Mail-Adresse oder eine neue
    /// Einladung (Rolle nach Kontaktart: Hausmeister, Handwerker, sonst Mieter).
    /// </summary>
    Task<Ergebnis> ZugangEinrichtenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Nur ohne Bezüge (Verträge …) möglich; sonst nach Ablauf der Frist anonymisieren.</summary>
    Task<Ergebnis> LoeschenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Personen zur Auswahl in Formularen (z. B. Mieter eines Vertrags), ohne Zugriffsprotokoll.</summary>
    Task<IReadOnlyList<PersonAuswahl>> AuswahlAsync(CancellationToken cancellationToken = default);
}

public enum Personenfilter
{
    Alle = 0,

    /// <summary>Privatpersonen: Mieter, Bürgen, Interessenten.</summary>
    Privat = 1,

    /// <summary>Kontakte mit Kontaktart (Hausmeister, Handwerker, Versorger …).</summary>
    Kontakte = 2,
}

public sealed record PersonenSuche(string? Text = null, Personenfilter Filter = Personenfilter.Alle, bool MitAnonymisierten = false);

public sealed record PersonUebersicht(
    Guid Id,
    string Anzeigename,
    string Sortiername,
    Kontaktart Kontaktart,
    string? Taetigkeit,
    string? Email,
    string? Telefon,
    string? Ort,
    IReadOnlyList<string> Rollen,
    bool HatZugang,
    bool Anonymisiert);

public sealed record PersonAuswahl(Guid Id, string Anzeigename, string? Ort);

public sealed record AnschriftStand(Zeitraum Gueltigkeit, string Einzeilig);

public sealed record BankverbindungAnsicht(Guid Id, string Iban, string? Kontoinhaber, string? Bic);

public sealed record ZugangAnsicht(Guid BenutzerId, string Email, string Rolle, bool Gesperrt);

public sealed record PersonDetails(
    Guid Id,
    PersonDaten Daten,
    string Anzeigename,
    AnschriftStand? Anschrift,
    IReadOnlyList<AnschriftStand> Anschriften,
    IReadOnlyList<BankverbindungAnsicht> Bankverbindungen,
    ZugangAnsicht? Zugang,
    IReadOnlyList<Personenbezug> Bezuege,
    DateTimeOffset? AnonymisiertAm);
