namespace MietHahn.Web.Components.Pages.Admin.Settings;

/// <summary>A flat entered in the setup wizard (name and living area).</summary>
public sealed class SetupUnitRow
{
    public string Name { get; set; } = "";

    public decimal? Area { get; set; }
}
