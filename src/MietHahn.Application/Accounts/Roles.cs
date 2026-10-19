namespace MietHahn.Application.Accounts;

/// <summary>Rollen laut docs/fachlichkeit.md → „Nutzer &amp; Rollen“.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Caretaker = "Caretaker";
    public const string Tradesperson = "Tradesperson";
    public const string Tenant = "Tenant";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Caretaker, Tradesperson, Tenant];

    /// <summary>Rollen mit Zugriff auf die Verwaltung: Passwort + Zwei-Faktor-Pflicht, kein Magic Link.</summary>
    public static readonly IReadOnlyList<string> RequiringTwoFactor = [Admin, Manager];

    /// <summary>Melden sich über ein Passwort an (Hausmeister optional auch per Magic Link).</summary>
    public static bool RequiresPassword(string role) => role is Admin or Manager or Caretaker;

    public static bool AllowsMagicLink(IEnumerable<string> roles) => !roles.Any(r => RequiringTwoFactor.Contains(r));

    public static string DisplayName(string role) => role switch
    {
        Admin => "Administrator",
        Manager => "Verwalter",
        Caretaker => "Hausmeister",
        Tradesperson => "Handwerker",
        Tenant => "Mieter",
        _ => role,
    };
}
