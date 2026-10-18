using MietHahn.Infrastructure;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMietHahnInfrastruktur(builder.Configuration);
builder.Services.AddRazorComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

await app.Services.MigriereDatenbankAsync();
await app.RunAsync();

/// <summary>Sichtbar für Tests (WebApplicationFactory, Architekturtests).</summary>
public partial class Program;
