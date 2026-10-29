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
            Primary = "#a73f17",
            PrimaryContrastText = "#ffffff",
            Secondary = "#77574d",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#6b5e2f",
            Info = "#3c5a73",
            Success = "#2e6b3a",
            Warning = "#7a5900",
            Error = "#ba1a1a",
            Background = "#fff8f6",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#231916",
            DrawerBackground = "#ffffff",
            DrawerText = "#231916",
            TextPrimary = "#231916",
            TextSecondary = "#53433e",
            ActionDefault = "#53433e",
            LinesDefault = "#d8c2bb",
            LinesInputs = "#85736d",
            TableLines = "#d8c2bb",
            Divider = "#d8c2bb",
            TableStriped = "#fff1ed",
            TableHover = "#fceae4",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#ffb59c",
            PrimaryContrastText = "#5c1900",
            Secondary = "#e7bdb0",
            SecondaryContrastText = "#2c150e",
            Tertiary = "#d7c68d",
            Info = "#a9c9e6",
            Success = "#9cd4a3",
            Warning = "#f0c04a",
            Error = "#ffb4ab",
            Background = "#1a110e",
            Surface = "#271d19",
            AppbarBackground = "#271d19",
            AppbarText = "#f1dfd9",
            DrawerBackground = "#271d19",
            DrawerText = "#f1dfd9",
            TextPrimary = "#f1dfd9",
            TextSecondary = "#d8c2bb",
            ActionDefault = "#d8c2bb",
            LinesDefault = "#53433e",
            LinesInputs = "#a08d86",
            TableLines = "#53433e",
            Divider = "#53433e",
            TableStriped = "#231915",
            TableHover = "#322723",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "12px" },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Roboto Flex", "Roboto", "system-ui", "Segoe UI", "sans-serif"] },
            Button = new ButtonTypography { TextTransform = "none", FontWeight = "600" },
        },
    };
}
