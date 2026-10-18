using MietHahn.Application.Gemeinsam;
using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Web.Autorisierung;
using MietHahn.Web.Components;
using MietHahn.Web.Konto;

var builder = WebApplication.CreateBuilder(args);

// Vor der Infrastruktur registrieren: dort wird nur ein Standard (System-Benutzer) ergänzt, falls nichts gesetzt ist.
builder.Services.AddScoped<IAktuellerBenutzer, HttpAktuellerBenutzer>();
builder.Services.AddMietHahnInfrastruktur(builder.Configuration);
builder.Services.AddMietHahnAnmeldung(builder.Configuration);
builder.Services.AddRazorComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseZweiFaktorPflicht();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>();
app.MapKontoEndpunkte();
app.MapDateiEndpunkte();

await app.Services.MigriereDatenbankAsync();
await app.RunAsync();

/// <summary>Sichtbar für Tests (WebApplicationFactory, Architekturtests).</summary>
public partial class Program;
