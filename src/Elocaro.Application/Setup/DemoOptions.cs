namespace Elocaro.Application.Setup;

/// <summary>
/// Settings of a public demo instance (<c>Demo__Enabled=true</c>). Only for a throw-away installation without real data: the
/// demo user is an administrator with a published password and without two-factor authentication.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    public string AdminEmail { get; set; } = "demo@elocaro.de";

    public string AdminPassword { get; set; } = "";
}
