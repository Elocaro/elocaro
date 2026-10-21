using MietHahn.Application.Common;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.AccessControl;
using MietHahn.Web.Account;
using MietHahn.Web.Components;
using MietHahn.Web.Deadlines;
using MietHahn.Web.Documents;
using MietHahn.Web.Finance;
using MietHahn.Web.Notices;
using MietHahn.Web.Operations;
using MietHahn.Web.People;
using MudBlazor.Services;

if (args.FirstOrDefault() == "healthcheck")
{
    return await OperationsExtensions.SelfCheckAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

// Register before the infrastructure: it only adds a default (system user) if nothing is set.
builder.Services.AddScoped<CircuitUser>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, CircuitUserHandler>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddMietHahnInfrastructure(builder.Configuration);
builder.Services.AddMietHahnSignIn(builder.Configuration);
builder.Services.AddMietHahnOperations();
builder.Services.AddTemplatePreviews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState(); // AuthorizeView in interactive pages
builder.Services.AddMudServices();
builder.Services.AddScoped<MietHahn.Web.Components.Shell.Notifier>();
builder.Services.AddScoped<MietHahn.Web.Components.Shell.Confirmations>();

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Configuration.GetValue("Hosting:HttpsRedirection", true))
{
    app.UseHttpsRedirection(); // in the container Caddy handles this (Hosting__HttpsRedirection=false)
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseTwoFactorRequirement();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
// Our own CSP (frame-ancestors 'none') applies; the framework header would be a second, duplicate one.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);
app.MapAccountEndpoints();
app.MapFileEndpoints();
app.MapHealthEndpoints();
app.MapExportEndpoint();
app.MapAccessReportEndpoint();
app.MapOpenItemsEndpoint();
app.MapNoticePdfEndpoint();
app.MapDatevEndpoints();
app.MapContractorFileEndpoint();
app.MapCalendarEndpoint();
app.MapAnlageVEndpoints();
app.MapDunningPreviewEndpoint();
app.MapTemplatePreviewEndpoint();
app.MapStatementPreviewEndpoint();

await app.Services.MigrateDatabaseAsync();
await app.RunAsync();
return 0;

/// <summary>Visible to tests (WebApplicationFactory, architecture tests).</summary>
public partial class Program;
