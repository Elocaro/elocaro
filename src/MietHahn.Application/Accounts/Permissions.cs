namespace MietHahn.Application.Accounts;

/// <summary>
/// Einzelrechte in der Verwaltung. Admins haben immer alle Rechte; Verwalter erhalten eine konfigurierbare Auswahl
/// (Standard: <see cref="ManagerDefault"/>).
/// </summary>
public enum AccessRight
{
    /// <summary>Objekte, Mieter, Verträge, Zähler, Dokumente.</summary>
    MasterData,

    /// <summary>Konten, Umsätze, Sollstellungen, Mahnwesen, Belege, Abrechnungen, Steuer.</summary>
    Finance,

    /// <summary>Konten einladen, sperren, Rollen und Rechte ändern.</summary>
    UserManagement,

    /// <summary>Technische Einstellungen: Mailversand, Jobs, Vorlagen, Vermieterprofil.</summary>
    System,
}

public static class Permissions
{
    public const string ClaimType = "miethahn:permission";

    public static readonly IReadOnlyList<AccessRight> All = Enum.GetValues<AccessRight>();

    public static readonly IReadOnlyList<AccessRight> ManagerDefault = [AccessRight.MasterData, AccessRight.Finance];

    public static string DisplayName(AccessRight permission) => permission switch
    {
        AccessRight.MasterData => "Stammdaten (Objekte, Mieter, Verträge)",
        AccessRight.Finance => "Finanzen (Bank, Belege, Abrechnungen, Steuer)",
        AccessRight.UserManagement => "Benutzerverwaltung",
        AccessRight.System => "System & Einstellungen",
        _ => permission.ToString(),
    };
}

/// <summary>Namen der Autorisierungsrichtlinien.</summary>
public static class Policies
{
    public const string Admin = "Admin";
    public const string Service = "Service";
    public const string Portal = "Portal";
    public const string MasterData = "Permission:MasterData";
    public const string Finance = "Permission:Finance";
    public const string UserManagement = "Permission:UserManagement";
    public const string System = "Permission:System";

    public static string For(AccessRight permission) => "Permission:" + permission;
}
