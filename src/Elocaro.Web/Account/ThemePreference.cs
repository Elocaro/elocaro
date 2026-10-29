namespace Elocaro.Web.Account;

/// <summary>
/// Display preference (system, light, dark) in a long-lived cookie. The page sets it as <c>data-theme</c> on the
/// root element; without a cookie the system setting (<c>prefers-color-scheme</c>) applies.
/// </summary>
internal static class ThemePreference
{
    public const string Cookie = "elocaro.theme";

    public const string System = "system";

    public static readonly IReadOnlyList<string> Allowed = ["system", "light", "dark"];

    /// <summary>The theme to force, or <c>null</c> to follow the system.</summary>
    public static string? Read(HttpContext? context) =>
        context is not null && context.Request.Cookies.TryGetValue(Cookie, out var value) && value is "light" or "dark"
            ? value
            : null;

    public static void Write(HttpContext context, string? theme)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (theme is "light" or "dark")
        {
            context.Response.Cookies.Append(Cookie, theme, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                HttpOnly = true,
                IsEssential = true,
                Secure = context.Request.IsHttps,
            });
        }
        else
        {
            context.Response.Cookies.Delete(Cookie);
        }
    }
}
