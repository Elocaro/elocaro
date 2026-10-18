using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Audit;
using MietHahn.Domain.Dateien;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Integration.Tests.Infrastruktur;
using MietHahn.Integration.Tests.Persistenz;

namespace MietHahn.Integration.Tests.Audit;

public class AuditLogTests(PostgresFixture postgres) : PersistenzTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Neuanlage_protokolliert_alle_gesetzten_Felder()
    {
        var eintrag = await AnlegenAsync(new TestEintrag
        {
            Name = "EG links",
            Betrag = 650m,
            Gueltigkeit = new Zeitraum(new DateOnly(2026, 1, 1)),
            Schlagworte = ["WG", "Balkon"],
        });

        var protokoll = Assert.Single(await HistorieAsync(eintrag.Id));
        Assert.Equal(AuditAktion.Angelegt, protokoll.Aktion);
        Assert.Equal("test", protokoll.Benutzer);
        Assert.Equal(Startzeit, protokoll.Zeitpunkt);
        Assert.Contains(new Feldaenderung("Name", null, "EG links"), protokoll.Aenderungen);
        Assert.Contains(new Feldaenderung("Betrag", null, "650"), protokoll.Aenderungen);
        Assert.Contains(new Feldaenderung("Gueltigkeit.Von", null, "2026-01-01"), protokoll.Aenderungen);
        Assert.Contains(new Feldaenderung("Schlagworte", null, "WG, Balkon"), protokoll.Aenderungen);
        Assert.DoesNotContain(protokoll.Aenderungen, a => a.Feld is "Iban" or "Gueltigkeit.Bis" or "ErstelltAm" or "Version");
    }

    [Fact]
    public async Task Aenderung_protokolliert_alten_und_neuen_Wert_mit_Benutzer()
    {
        var eintrag = await AnlegenAsync(new TestEintrag { Name = "Kaltmiete", Betrag = 650m });

        Uhr.Advance(TimeSpan.FromDays(1));
        Benutzer.Kennung = "partnerin";
        await using (var db = NeuerContext())
        {
            var geladen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
            geladen.Betrag = 700m;
            geladen.Gueltigkeit = geladen.Gueltigkeit.BeendenZum(new DateOnly(2026, 12, 31));
            await db.SaveChangesAsync(Ct);
        }

        var aenderung = (await HistorieAsync(eintrag.Id))[0];
        Assert.Equal(AuditAktion.Geaendert, aenderung.Aktion);
        Assert.Equal("partnerin", aenderung.Benutzer);
        Assert.Equal(Startzeit.AddDays(1), aenderung.Zeitpunkt);
        Assert.Equal(
            [new Feldaenderung("Betrag", "650", "700"), new Feldaenderung("Gueltigkeit.Bis", null, "2026-12-31")],
            aenderung.Aenderungen.OrderBy(a => a.Feld, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Speichern_ohne_echte_Aenderung_erzeugt_keinen_Eintrag()
    {
        var eintrag = await AnlegenAsync(new TestEintrag { Name = "unverändert", Betrag = 1m });

        await using (var db = NeuerContext())
        {
            var geladen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
            geladen.Betrag = 1m;
            geladen.Schlagworte = [];
            await db.SaveChangesAsync(Ct);
        }

        Assert.Single(await HistorieAsync(eintrag.Id));
    }

    [Fact]
    public async Task Vertrauliche_Felder_werden_maskiert()
    {
        var eintrag = await AnlegenAsync(new TestEintrag { Name = "Konto", Iban = "DE02120300000000202051" });

        await using (var db = NeuerContext())
        {
            (await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct)).Iban = "DE89370400440532013000";
            await db.SaveChangesAsync(Ct);
        }

        var historie = await HistorieAsync(eintrag.Id);
        Assert.Contains(new Feldaenderung("Iban", AuditErfassung.Maskiert, AuditErfassung.Maskiert), historie[0].Aenderungen);
        Assert.Contains(new Feldaenderung("Iban", null, AuditErfassung.Maskiert), historie[1].Aenderungen);
        await using var pruefung = NeuerContext();
        var roh = await pruefung.Set<AuditEintrag>().Select(a => a.AenderungenJson).ToListAsync(Ct);
        Assert.DoesNotContain(roh, j => j.Contains("DE02", StringComparison.Ordinal) || j.Contains("DE89", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Loeschen_wird_als_Loeschung_protokolliert()
    {
        var eintrag = await AnlegenAsync(new TestEintrag { Name = "weg" });

        await using (var db = NeuerContext())
        {
            db.Eintraege.Remove(await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        var loeschung = (await HistorieAsync(eintrag.Id))[0];
        Assert.Equal(AuditAktion.Geloescht, loeschung.Aktion);
        Assert.Empty(loeschung.Aenderungen);
    }

    [Fact]
    public async Task Technische_Entitaeten_werden_nicht_protokolliert()
    {
        await using (var db = NeuerContext())
        {
            db.Add(new MietHahn.Domain.Kommunikation.AusgehendeMail(["a@example.de"], "Betreff", "<p/>", "", Startzeit));
            db.Add(new Datei("beleg.pdf", "application/pdf", 10, new string('a', 64)));
            await db.SaveChangesAsync(Ct);
        }

        await using var pruefung = NeuerContext();
        var typen = await pruefung.Set<AuditEintrag>().Select(a => a.EntitaetTyp).ToListAsync(Ct);
        Assert.Equal(["Datei"], typen);
    }

    [Fact]
    public async Task Audit_Eintrag_wird_mit_der_Aenderung_zurueckgerollt()
    {
        await using var db = NeuerContext();
        await using var transaktion = await db.Database.BeginTransactionAsync(Ct);
        db.Eintraege.Add(new TestEintrag { Name = "abgebrochen" });
        await db.SaveChangesAsync(Ct);
        await transaktion.RollbackAsync(Ct);

        await using var pruefung = NeuerContext();
        Assert.False(await pruefung.Set<AuditEintrag>().AnyAsync(Ct));
    }

    private async Task<TestEintrag> AnlegenAsync(TestEintrag eintrag)
    {
        await using var db = NeuerContext();
        db.Eintraege.Add(eintrag);
        await db.SaveChangesAsync(Ct);
        return eintrag;
    }

    private async Task<IReadOnlyList<AuditEintragAnsicht>> HistorieAsync(Guid id)
    {
        await using var db = NeuerContext();
        return await new AenderungsHistorie(db).LadenAsync(nameof(TestEintrag), id, Ct);
    }
}
