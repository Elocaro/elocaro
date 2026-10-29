using Microsoft.AspNetCore.Identity;

namespace Elocaro.Infrastructure.Identity;

/// <summary>User account (ASP.NET Core Identity). The email address is also the user name.</summary>
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

    /// <summary>Blocked accounts cannot sign in (in addition to the temporary lockout).</summary>
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
