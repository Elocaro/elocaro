namespace MietHahn.Application.Accounts;

/// <summary>Roles according to docs/fachlichkeit.md → "Nutzer &amp; Rollen".</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Caretaker = "Caretaker";
    public const string Tradesperson = "Tradesperson";
    public const string Tenant = "Tenant";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Caretaker, Tradesperson, Tenant];

    /// <summary>Roles with access to the administration: password + two-factor required, no magic link.</summary>
    public static readonly IReadOnlyList<string> RequiringTwoFactor = [Admin, Manager];

    /// <summary>Sign in with a password (caretakers optionally also via magic link).</summary>
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
