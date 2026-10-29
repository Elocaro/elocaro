namespace Elocaro.Application.Admin;

/// <summary>
/// Settings of a public demo instance (<c>Demo__Enabled=true</c>). Only for a throw-away installation without real data: the
/// demo users have a published password and no two-factor authentication, and passwords cannot be changed.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>The demo users besides the administrator, one per role; all share the password of the administrator.</summary>
    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("verwalter@elocaro.de", "Verwalter", "Vera Verwalter", "Stammdaten und Finanzen, ohne Benutzerverwaltung"),
        new("hausmeister@elocaro.de", "Hausmeister", "Hans Hausmeister", "Tickets und Aufgaben des Hauses"),
        new("handwerker@elocaro.de", "Handwerker", "Heinz Handwerker", "nur zugewiesene Tickets"),
        new("eigentuemer@elocaro.de", "Eigentümer", "Erika Eigentümer", "lesender Blick auf das eigene Objekt"),
        new("mieter@elocaro.de", "Mieter", "Anna Musterfrau", "Mieterportal mit Vertrag und Konto"),
    ];

    public bool Enabled { get; set; }

    public string AdminEmail { get; set; } = "demo@elocaro.de";

    public string AdminPassword { get; set; } = "";
}

/// <summary>An account of the demo instance, shown on the sign-in page.</summary>
public sealed record DemoUser(string Email, string Role, string DisplayName, string Description);
