using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Kommunikation;

public enum MailStatus
{
    Ausstehend = 0,
    Gesendet = 1,
    Fehlgeschlagen = 2,
}

/// <summary>
/// Eine E-Mail in der Outbox. Wird beim Einreihen fertig gerendert gespeichert (Nachweis, was verschickt wurde)
/// und von einem Hintergrundjob mit Wiederholungen zugestellt.
/// </summary>
public sealed class AusgehendeMail : Entitaet
{
    /// <summary>Nach so vielen Fehlversuchen wird nicht mehr automatisch wiederholt.</summary>
    public const int MaxVersuche = 8;

    public const int MaxFehlerLaenge = 2000;

    private static readonly TimeSpan MaxWartezeit = TimeSpan.FromHours(6);

    private AusgehendeMail()
    {
        Betreff = "";
        Html = "";
        Text = "";
    }

    public AusgehendeMail(
        IEnumerable<string> an,
        string betreff,
        string html,
        string text,
        DateTimeOffset jetzt,
        IEnumerable<Guid>? anhangDateiIds = null,
        string? vorlage = null,
        string? bezug = null)
    {
        ArgumentNullException.ThrowIfNull(an);
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);

        An = an.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (An.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Empfänger ist erforderlich.", nameof(an));
        }

        if (An.FirstOrDefault(a => !a.Contains('@', StringComparison.Ordinal)) is { } ungueltig)
        {
            throw new ArgumentException($"Ungültige E-Mail-Adresse: {ungueltig}", nameof(an));
        }

        Betreff = betreff;
        Html = html;
        Text = text;
        AnhangDateiIds = anhangDateiIds?.ToList() ?? [];
        Vorlage = vorlage;
        Bezug = bezug;
        Status = MailStatus.Ausstehend;
        NaechsterVersuchAm = jetzt;
    }

    public List<string> An { get; private set; } = [];

    public string Betreff { get; private set; }

    public string Html { get; private set; }

    public string Text { get; private set; }

    public List<Guid> AnhangDateiIds { get; private set; } = [];

    /// <summary>Name der verwendeten Vorlage (zur Nachvollziehbarkeit).</summary>
    public string? Vorlage { get; private set; }

    /// <summary>Optionaler fachlicher Bezug, z. B. <c>Abrechnung:…</c>, für den Versandnachweis.</summary>
    public string? Bezug { get; private set; }

    public MailStatus Status { get; private set; }

    public int Versuche { get; private set; }

    public DateTimeOffset NaechsterVersuchAm { get; private set; }

    public DateTimeOffset? GesendetAm { get; private set; }

    public string? LetzterFehler { get; private set; }

    public void MarkiereGesendet(DateTimeOffset jetzt)
    {
        Status = MailStatus.Gesendet;
        GesendetAm = jetzt;
        Versuche++;
        LetzterFehler = null;
    }

    public void MarkiereFehlversuch(DateTimeOffset jetzt, string fehler)
    {
        Versuche++;
        LetzterFehler = fehler is { Length: > MaxFehlerLaenge } ? fehler[..MaxFehlerLaenge] : fehler;

        if (Versuche >= MaxVersuche)
        {
            Status = MailStatus.Fehlgeschlagen;
            return;
        }

        NaechsterVersuchAm = jetzt + Wartezeit(Versuche);
    }

    /// <summary>Manuelles erneutes Versenden einer fehlgeschlagenen Mail.</summary>
    public void ErneutVersuchen(DateTimeOffset jetzt)
    {
        if (Status == MailStatus.Gesendet)
        {
            throw new InvalidOperationException("Die Mail wurde bereits gesendet.");
        }

        Status = MailStatus.Ausstehend;
        Versuche = 0;
        NaechsterVersuchAm = jetzt;
    }

    /// <summary>Exponentieller Backoff: 1, 2, 4, 8 … Minuten, höchstens 6 Stunden.</summary>
    public static TimeSpan Wartezeit(int fehlversuche)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fehlversuche);
        var minuten = Math.Pow(2, fehlversuche - 1);
        return minuten >= MaxWartezeit.TotalMinutes ? MaxWartezeit : TimeSpan.FromMinutes(minuten);
    }
}
