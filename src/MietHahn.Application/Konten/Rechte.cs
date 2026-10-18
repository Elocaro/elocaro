namespace MietHahn.Application.Konten;

/// <summary>
/// Einzelrechte in der Verwaltung. Admins haben immer alle Rechte; Verwalter erhalten eine konfigurierbare Auswahl
/// (Standard: <see cref="VerwalterStandard"/>).
/// </summary>
public enum Recht
{
    /// <summary>Objekte, Mieter, Verträge, Zähler, Dokumente.</summary>
    Stammdaten,

    /// <summary>Konten, Umsätze, Sollstellungen, Mahnwesen, Belege, Abrechnungen, Steuer.</summary>
    Finanzen,

    /// <summary>Konten einladen, sperren, Rollen und Rechte ändern.</summary>
    Benutzerverwaltung,

    /// <summary>Technische Einstellungen: Mailversand, Jobs, Vorlagen, Vermieterprofil.</summary>
    System,
}

public static class Rechte
{
    public const string ClaimTyp = "miethahn:recht";

    public static readonly IReadOnlyList<Recht> Alle = Enum.GetValues<Recht>();

    public static readonly IReadOnlyList<Recht> VerwalterStandard = [Recht.Stammdaten, Recht.Finanzen];

    public static string Anzeigename(Recht recht) => recht switch
    {
        Recht.Stammdaten => "Stammdaten (Objekte, Mieter, Verträge)",
        Recht.Finanzen => "Finanzen (Bank, Belege, Abrechnungen, Steuer)",
        Recht.Benutzerverwaltung => "Benutzerverwaltung",
        Recht.System => "System & Einstellungen",
        _ => recht.ToString(),
    };
}

/// <summary>Namen der Autorisierungsrichtlinien.</summary>
public static class Richtlinien
{
    public const string Verwaltung = "Verwaltung";
    public const string Service = "Service";
    public const string Portal = "Portal";
    public const string Stammdaten = "Recht:Stammdaten";
    public const string Finanzen = "Recht:Finanzen";
    public const string Benutzerverwaltung = "Recht:Benutzerverwaltung";
    public const string System = "Recht:System";

    public static string Fuer(Recht recht) => "Recht:" + recht;
}
