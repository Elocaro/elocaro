using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Dateien;
using MietHahn.Application.Kommunikation;
using MietHahn.Domain.Kommunikation;
using MietHahn.Infrastructure.Mail;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Vorlagen;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Mail;

public class MailVersandTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private InfrastrukturUmgebung umgebung = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => umgebung = await InfrastrukturUmgebung.ErstellenAsync(postgres, mailpit.Konfiguration());

    public async ValueTask DisposeAsync() => await umgebung.DisposeAsync();

    [Fact]
    public async Task Eingereihte_Mail_wird_gerendert_im_Layout_gespeichert()
    {
        var id = await EinreihenAsync(new MailAuftrag(["mieter@example.de"], "test", new { GesendetAm = new DateOnly(2026, 3, 1), AusgeloestVon = "<admin>" }, Bezug: "Test:1"));

        var mail = await MailAsync(id);
        Assert.Equal(MailStatus.Ausstehend, mail.Status);
        Assert.Equal("Testnachricht von MietHahn", mail.Betreff);
        Assert.Equal("test", mail.Vorlage);
        Assert.Equal("Test:1", mail.Bezug);
        Assert.Contains("<!DOCTYPE html>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Hausverwaltung Test", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;admin&gt;", mail.Html, StringComparison.Ordinal);
        Assert.Contains("dies ist eine Testnachricht", mail.Text, StringComparison.Ordinal);
        Assert.Contains("<admin>", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", mail.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../geheim")]
    [InlineData("Test")]
    [InlineData("")]
    public async Task Ungueltiger_Vorlagenname_wird_abgelehnt(string vorlage)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => EinreihenAsync(new MailAuftrag(["a@example.de"], vorlage)));
    }

    [Fact]
    public async Task Fehlendes_Modell_fuer_Vorlage_wird_gemeldet()
    {
        await Assert.ThrowsAsync<VorlagenFehlerException>(() => EinreihenAsync(new MailAuftrag(["a@example.de"], "test")));
    }

    [Fact]
    public async Task Outbox_stellt_Mail_mit_Anhang_per_SMTP_zu()
    {
        var empfaenger = $"mieter-{Guid.NewGuid():N}@example.de";
        Guid anhangId;
        await using (var scope = umgebung.Services.CreateAsyncScope())
        {
            using var pdf = new MemoryStream(TestDateien.Pdf("Abrechnung"));
            anhangId = (await scope.ServiceProvider.GetRequiredService<IDateiablage>().SpeichernAsync(pdf, "Abrechnung 2025.pdf", Ct)).Id;
        }

        var id = await EinreihenAsync(new MailAuftrag([empfaenger], "test", TestModell(), [anhangId]));
        var gesendet = await ZustellenAsync();

        Assert.Equal(1, gesendet);
        var mail = await MailAsync(id);
        Assert.Equal(MailStatus.Gesendet, mail.Status);
        Assert.Equal(InfrastrukturUmgebung.Startzeit, mail.GesendetAm);

        var zugestellt = Assert.Single(await mailpit.NachrichtenAnAsync(empfaenger, Ct));
        Assert.Equal("Testnachricht von MietHahn", zugestellt.Betreff);
        Assert.Equal("verwaltung@miethahn.test", zugestellt.Von.Adresse);
        Assert.Equal("Hausverwaltung Test", zugestellt.Von.Name);
        Assert.Equal("vermieter@miethahn.test", Assert.Single(zugestellt.AntwortAn).Adresse);
        Assert.Contains("dies ist eine Testnachricht", zugestellt.Html, StringComparison.Ordinal);
        Assert.Contains("dies ist eine Testnachricht", zugestellt.Text, StringComparison.Ordinal);
        var anhang = Assert.Single(zugestellt.Anhaenge);
        Assert.Equal("Abrechnung 2025.pdf", anhang.Dateiname);
        Assert.Equal("application/pdf", anhang.Typ);
    }

    [Fact]
    public async Task Bereits_gesendete_Mail_wird_nicht_erneut_zugestellt()
    {
        var empfaenger = $"einmal-{Guid.NewGuid():N}@example.de";
        await EinreihenAsync(new MailAuftrag([empfaenger], "test", TestModell()));

        Assert.Equal(1, await ZustellenAsync());
        Assert.Equal(0, await ZustellenAsync());
        Assert.Single(await mailpit.NachrichtenAnAsync(empfaenger, Ct));
    }

    [Fact]
    public async Task Bei_nicht_erreichbarem_Server_wird_mit_Backoff_wiederholt()
    {
        var konfiguration = mailpit.Konfiguration();
        konfiguration["Mail:Smtp:Port"] = "1"; // dort lauscht niemand
        await using var ohneServer = await InfrastrukturUmgebung.ErstellenAsync(postgres, konfiguration);

        Guid id;
        await using (var scope = ohneServer.Services.CreateAsyncScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IMailVersand>().EinreihenAsync(new MailAuftrag(["a@example.de"], "test", TestModell()), Ct);
        }

        Assert.Equal(0, await ZustellenAsync(ohneServer));
        var mail = await MailAsync(id, ohneServer);
        Assert.Equal(MailStatus.Ausstehend, mail.Status);
        Assert.Equal(1, mail.Versuche);
        Assert.NotNull(mail.LetzterFehler);
        Assert.Equal(InfrastrukturUmgebung.Startzeit.AddMinutes(1), mail.NaechsterVersuchAm);

        // Vor Ablauf der Wartezeit wird nichts versucht, danach erneut.
        ohneServer.Uhr.Advance(TimeSpan.FromSeconds(30));
        await ZustellenAsync(ohneServer);
        Assert.Equal(1, (await MailAsync(id, ohneServer)).Versuche);

        ohneServer.Uhr.Advance(TimeSpan.FromSeconds(31));
        await ZustellenAsync(ohneServer);
        Assert.Equal(2, (await MailAsync(id, ohneServer)).Versuche);
    }

    [Fact]
    public void Textfassung_wird_aus_HTML_abgeleitet()
    {
        var text = MailVersand.HtmlZuText("<h1>Titel</h1><p>Hallo &amp; willkommen,<br>zweite Zeile</p><p><a href=\"https://miethahn.test/x\">Link</a></p>");

        Assert.Equal("Titel\nHallo & willkommen,\nzweite Zeile\nLink (https://miethahn.test/x)", text);
    }

    private static object TestModell() => new { GesendetAm = new DateOnly(2026, 3, 1), AusgeloestVon = "admin" };

    private async Task<Guid> EinreihenAsync(MailAuftrag auftrag)
    {
        await using var scope = umgebung.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMailVersand>().EinreihenAsync(auftrag, Ct);
    }

    private Task<int> ZustellenAsync() => ZustellenAsync(umgebung);

    private static async Task<int> ZustellenAsync(InfrastrukturUmgebung u)
    {
        await using var scope = u.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MailOutbox>().ZustellenAsync(Ct);
    }

    private Task<AusgehendeMail> MailAsync(Guid id) => MailAsync(id, umgebung);

    private static async Task<AusgehendeMail> MailAsync(Guid id, InfrastrukturUmgebung u)
    {
        await using var scope = u.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<AusgehendeMail>().AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }
}
