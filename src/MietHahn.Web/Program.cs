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

// Vor der Infrastruktur registrieren: dort wird nur ein Standard (System-Benutzer) ergänzt, falls nichts gesetzt ist.
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
    app.UseHttpsRedirection(); // im Container übernimmt das Caddy (Hosting__HttpsRedirection=false)
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

await app.Services.MigrateDatabaseAsync();
await app.RunAsync();
return 0;

/// <summary>Sichtbar für Tests (WebApplicationFactory, Architekturtests).</summary>
public partial class Program;
