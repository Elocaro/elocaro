using Microsoft.AspNetCore.Identity;

namespace MietHahn.Infrastructure.Identitaet;

/// <summary>Benutzerkonto (ASP.NET Core Identity). Die E-Mail-Adresse ist zugleich der Benutzername.</summary>
public sealed class Benutzer : IdentityUser<Guid>
{
    public Benutzer()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string Anzeigename { get; set; } = "";

    public DateTimeOffset ErstelltAm { get; set; }

    public DateTimeOffset? LetzteAnmeldungAm { get; set; }

    /// <summary>Gesperrte Konten können sich nicht anmelden (zusätzlich zur temporären Lockout-Sperre).</summary>
    public bool Gesperrt { get; set; }
}

public sealed class Rolle : IdentityRole<Guid>
{
    public Rolle()
    {
        Id = Guid.CreateVersion7();
    }

    public Rolle(string name)
        : this()
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
    }
}
