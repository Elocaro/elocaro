namespace MietHahn.Web.Account;

/// <summary>Konfigurationsabschnitt <c>Anmeldung</c>.</summary>
public sealed class SignInOptions
{
    public const string Section = "SignIn";

    /// <summary>Erlaubte POST-Anfragen auf <c>/konto/*</c> je IP-Adresse und Minute (Schutz vor Durchprobieren).</summary>
    public int RequestsPerMinute { get; set; } = 10;

    /// <summary>Wie lange eine Sitzung mit „Angemeldet bleiben“ bzw. per Anmeldelink gültig ist.</summary>
    public int SessionDays { get; set; } = 14;
}
