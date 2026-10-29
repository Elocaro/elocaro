namespace Elocaro.Application.Accounts;

/// <summary>
/// Individual permissions in the administration. Admins always have all permissions; managers get a configurable selection
/// (default: <see cref="ManagerDefault"/>).
/// </summary>
public enum AccessRight
{
    /// <summary>Properties, tenants, leases, meters, documents.</summary>
    MasterData,

    /// <summary>Accounts, transactions, charges, dunning, receipts, statements, tax.</summary>
    Finance,

    /// <summary>Invite, lock accounts, change roles and permissions.</summary>
    UserManagement,

    /// <summary>Technical settings: mail delivery, jobs, templates, landlord profile.</summary>
    System,
}

public static class Permissions
{
    public const string ClaimType = "elocaro:permission";

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

/// <summary>Names of the authorization policies.</summary>
public static class Policies
{
    public const string Admin = "Admin";
    public const string Service = "Service";
    public const string Portal = "Portal";
    public const string Owner = "Owner";
    public const string MasterData = "Permission:MasterData";
    public const string Finance = "Permission:Finance";
    public const string UserManagement = "Permission:UserManagement";
    public const string System = "Permission:System";

    public static string For(AccessRight permission) => "Permission:" + permission;
}
