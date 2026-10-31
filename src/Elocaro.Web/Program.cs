using Elocaro.Application.Common;
using Elocaro.Infrastructure;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Setup;
using Elocaro.Web.AccessControl;
using Elocaro.Web.Account;
using Elocaro.Web.Admin;
using Elocaro.Web.Components;
using Elocaro.Web.Deadlines;
using Elocaro.Web.Documents;
using Elocaro.Web.Finance;
using Elocaro.Web.Notices;
using Elocaro.Web.Operations;
using Elocaro.Web.People;
using Elocaro.Web.Push;
using Elocaro.Web.Signatures;
using Elocaro.Web.Tickets;
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
builder.Services.AddElocaroInfrastructure(builder.Configuration);
builder.Services.AddElocaroSignIn(builder.Configuration);
builder.Services.AddElocaroOperations();
builder.Services.AddTemplatePreviews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState(); // AuthorizeView in interactive pages
builder.Services.AddMudServices();
builder.Services.AddScoped<Elocaro.Web.Components.Shell.Notifier>();
builder.Services.AddScoped<Elocaro.Web.Components.Shell.Confirmations>();

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
app.UseModuleGate();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous().WithMetadata(new AccountExtensions.StaticFileEndpoint());
// Our own CSP (frame-ancestors 'none') applies; the framework header would be a second, duplicate one.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);
app.MapAccountEndpoints();
app.MapFileEndpoints();
app.MapHealthEndpoints();
app.MapExportEndpoint();
app.MapEmergencyFolderEndpoint();
app.MapAccessReportEndpoint();
app.MapOpenItemsEndpoint();
app.MapImportTemplateEndpoint();
app.MapCareChecksEndpoint();
app.MapNoticePdfEndpoint();
app.MapDatevEndpoints();
app.MapContractorFileEndpoint();
app.MapSignatureDocumentEndpoint();
app.MapPushEndpoints();
app.MapCalendarEndpoint();
app.MapAnlageVEndpoints();
app.MapDunningPreviewEndpoint();
app.MapTemplatePreviewEndpoint();
app.MapStatementPreviewEndpoint();
app.MapStatementReceiptEndpoint();

await app.Services.MigrateDatabaseAsync();
await app.Services.PrepareDemoAsync();
await app.RunAsync();
return 0;

/// <summary>Visible to tests (WebApplicationFactory, architecture tests).</summary>
public partial class Program;
