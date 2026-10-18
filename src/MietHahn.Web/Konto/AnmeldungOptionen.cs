namespace MietHahn.Web.Konto;

/// <summary>Konfigurationsabschnitt <c>Anmeldung</c>.</summary>
public sealed class AnmeldungOptionen
{
    public const string Abschnitt = "Anmeldung";

    /// <summary>Erlaubte POST-Anfragen auf <c>/konto/*</c> je IP-Adresse und Minute (Schutz vor Durchprobieren).</summary>
    public int AnfragenProMinute { get; set; } = 10;

    /// <summary>Wie lange eine Sitzung mit „Angemeldet bleiben“ bzw. per Anmeldelink gültig ist.</summary>
    public int SitzungTage { get; set; } = 14;
}
