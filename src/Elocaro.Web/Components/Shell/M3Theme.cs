using MudBlazor;

namespace Elocaro.Web.Components.Shell;

/// <summary>
/// The Material 3 (Expressive) look for MudBlazor. The values mirror the <c>--md-*</c> roles in
/// <c>wwwroot/app.css</c>; keep both in sync (docs/ui.md).
/// </summary>
internal static class M3Theme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#146d29",
            PrimaryContrastText = "#ffffff",
            Secondary = "#546252",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#33666e",
            Info = "#3c5a73",
            Success = "#486815",
            Warning = "#7a5900",
            Error = "#ba1a1a",
            Background = "#f8faf3",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#191c18",
            DrawerBackground = "#ffffff",
            DrawerText = "#191c18",
            TextPrimary = "#191c18",
            TextSecondary = "#424940",
            ActionDefault = "#424940",
            LinesDefault = "#c2c9bd",
            LinesInputs = "#72796f",
            TableLines = "#c2c9bd",
            Divider = "#c2c9bd",
            TableStriped = "#f2f4ed",
            TableHover = "#ecefe7",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#84da88",
            PrimaryContrastText = "#00390f",
            Secondary = "#bbcbb7",
            SecondaryContrastText = "#263425",
            Tertiary = "#9dcfd8",
            Info = "#a9c9e6",
            Success = "#add374",
            Warning = "#f0c04a",
            Error = "#ffb4ab",
            Background = "#111410",
            Surface = "#1d211c",
            AppbarBackground = "#1d211c",
            AppbarText = "#e1e3dc",
            DrawerBackground = "#1d211c",
            DrawerText = "#e1e3dc",
            TextPrimary = "#e1e3dc",
            TextSecondary = "#c2c9bd",
            ActionDefault = "#c2c9bd",
            LinesDefault = "#424940",
            LinesInputs = "#8c9388",
            TableLines = "#424940",
            Divider = "#424940",
            TableStriped = "#191c18",
            TableHover = "#1d211c",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "12px" },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Roboto Flex", "Roboto", "system-ui", "Segoe UI", "sans-serif"] },
            Button = new ButtonTypography { TextTransform = "none", FontWeight = "600" },
        },
    };
}
