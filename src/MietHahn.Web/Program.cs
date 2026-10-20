using MietHahn.Application.Common;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Web.AccessControl;
using MietHahn.Web.Account;
using MietHahn.Web.Components;
using MietHahn.Web.Finance;
using MietHahn.Web.Operations;
using MietHahn.Web.People;

if (args.FirstOrDefault() == "healthcheck")
{
    return await OperationsExtensions.SelfCheckAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

// Register before the infrastructure: it only adds a default (system user) if nothing is set.
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddMietHahnInfrastructure(builder.Configuration);
builder.Services.AddMietHahnSignIn(builder.Configuration);
builder.Services.AddMietHahnOperations();
builder.Services.AddRazorComponents();

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
app.MapRazorComponents<App>();
app.MapAccountEndpoints();
app.MapFileEndpoints();
app.MapHealthEndpoints();
app.MapExportEndpoint();
app.MapAccessReportEndpoint();
app.MapOpenItemsEndpoint();
app.MapAnlageVEndpoints();
app.MapDunningPreviewEndpoint();
app.MapStatementPreviewEndpoint();

await app.Services.MigrateDatabaseAsync();
await app.RunAsync();
return 0;

/// <summary>Visible to tests (WebApplicationFactory, architecture tests).</summary>
public partial class Program;
