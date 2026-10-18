using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Web.Autorisierung;
using MietHahn.Web.Betrieb;
using MietHahn.Web.Components;
using MietHahn.Web.Konto;

if (args.FirstOrDefault() == "healthcheck")
{
    return await BetriebErweiterungen.SelbstpruefungAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

// Vor der Infrastruktur registrieren: dort wird nur ein Standard (System-Benutzer) ergänzt, falls nichts gesetzt ist.
builder.Services.AddScoped<IAktuellerBenutzer, HttpAktuellerBenutzer>();
builder.Services.AddMietHahnInfrastruktur(builder.Configuration);
builder.Services.AddMietHahnAnmeldung(builder.Configuration);
builder.Services.AddMietHahnBetrieb();
builder.Services.AddRazorComponents();

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSicherheitsHeader();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Configuration.GetValue("Betrieb:HttpsUmleitung", true))
{
    app.UseHttpsRedirection(); // im Container übernimmt das Caddy (Betrieb__HttpsUmleitung=false)
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseZweiFaktorPflicht();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>();
app.MapKontoEndpunkte();
app.MapDateiEndpunkte();
app.MapHealthEndpunkte();
app.MapExportEndpunkt();

await app.Services.MigriereDatenbankAsync();
await app.RunAsync();
return 0;

/// <summary>Sichtbar für Tests (WebApplicationFactory, Architekturtests).</summary>
public partial class Program;
