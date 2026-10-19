using Microsoft.AspNetCore.Identity;

namespace MietHahn.Infrastructure.Identity;

/// <summary>Benutzerkonto (ASP.NET Core Identity). Die E-Mail-Adresse ist zugleich der Benutzername.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Gesperrte Konten können sich nicht anmelden (zusätzlich zur temporären Lockout-Sperre).</summary>
    public bool Locked { get; set; }
}

public sealed class Role : IdentityRole<Guid>
{
    public Role()
    {
        Id = Guid.CreateVersion7();
    }

    public Role(string name)
        : this()
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
    }
}
