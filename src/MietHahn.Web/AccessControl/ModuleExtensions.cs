using Microsoft.AspNetCore.Authorization;
using MietHahn.Application.Accounts;
using MietHahn.Application.Admin;

namespace MietHahn.Web.AccessControl;

public static class ModuleExtensions
{
    private const string WizardPath = "/verwaltung/einrichtung";

    /// <summary>
    /// Answers pages and downloads of switched-off modules with 404, as if they did not exist, and leads admins
    /// from the dashboard to the setup wizard until it is completed or put off.
    /// </summary>
    public static IApplicationBuilder UseModuleGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var modules = context.RequestServices.GetRequiredService<IModuleState>();
            await modules.EnsureLoadedAsync(context.RequestAborted);

            if (!modules.SetupCompleted
                && HttpMethods.IsGet(context.Request.Method)
                && context.Request.Path.Equals("/verwaltung", StringComparison.OrdinalIgnoreCase)
                && (await context.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(context.User, Policies.System)).Succeeded)
            {
                context.Response.Redirect(WizardPath);
                return;
            }

            var path = context.Request.Path.Value ?? "";
            if ((ModuleCatalog.OwnerOf(path) is { } owner && !modules.IsEnabled(owner.Module))
                || (ModuleCatalog.OptionOf(path) is { } option && !modules.IsVisible(option.Option)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
}
