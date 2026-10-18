using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Jobs;

/// <summary>Protokolleintrag einer Jobausführung (für die Admin-Übersicht).</summary>
[OhneAudit]
public sealed class JobLauf : Entitaet
{
    private JobLauf()
    {
        JobName = "";
    }

    public JobLauf(string jobName, DateTimeOffset gestartetAm)
    {
        JobName = jobName;
        GestartetAm = gestartetAm;
    }

    public string JobName { get; private set; }

    public DateTimeOffset GestartetAm { get; private set; }

    public DateTimeOffset? BeendetAm { get; private set; }

    public bool? Erfolgreich { get; private set; }

    public string? Fehler { get; private set; }

    public void Beenden(DateTimeOffset beendetAm, Exception? fehler)
    {
        BeendetAm = beendetAm;
        Erfolgreich = fehler is null;
        Fehler = fehler is null ? null : Kuerzen(fehler.GetBaseException().Message, 2000);
    }

    private static string Kuerzen(string text, int laenge) => text.Length > laenge ? text[..laenge] : text;
}
