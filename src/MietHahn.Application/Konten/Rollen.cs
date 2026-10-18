namespace MietHahn.Application.Konten;

/// <summary>Rollen laut docs/fachlichkeit.md → „Nutzer &amp; Rollen“.</summary>
public static class Rollen
{
    public const string Admin = "Admin";
    public const string Verwalter = "Verwalter";
    public const string Hausmeister = "Hausmeister";
    public const string Handwerker = "Handwerker";
    public const string Mieter = "Mieter";

    public static readonly IReadOnlyList<string> Alle = [Admin, Verwalter, Hausmeister, Handwerker, Mieter];

    /// <summary>Rollen mit Zugriff auf die Verwaltung: Passwort + Zwei-Faktor-Pflicht, kein Magic Link.</summary>
    public static readonly IReadOnlyList<string> MitZweiFaktorPflicht = [Admin, Verwalter];

    /// <summary>Melden sich über ein Passwort an (Hausmeister optional auch per Magic Link).</summary>
    public static bool BrauchtPasswort(string rolle) => rolle is Admin or Verwalter or Hausmeister;

    public static bool DarfMagicLink(IEnumerable<string> rollen) => !rollen.Any(r => MitZweiFaktorPflicht.Contains(r));

    public static string Anzeigename(string rolle) => rolle switch
    {
        Admin => "Administrator",
        Verwalter => "Verwalter",
        Hausmeister => "Hausmeister",
        Handwerker => "Handwerker",
        Mieter => "Mieter",
        _ => rolle,
    };
}
