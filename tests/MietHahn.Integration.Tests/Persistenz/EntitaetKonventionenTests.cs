using Microsoft.EntityFrameworkCore;
using MietHahn.Integration.Tests.Infrastruktur;

namespace MietHahn.Integration.Tests.Persistenz;

public class EntitaetKonventionenTests(PostgresFixture postgres) : PersistenzTestBasis(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entitaet_wird_gespeichert_und_mit_Audit_Feldern_gelesen()
    {
        var eintrag = new TestEintrag { Name = "Erdgeschoss links", Betrag = 812.50m, Von = new DateOnly(2026, 1, 1) };
        await using (var db = NeuerContext())
        {
            db.Eintraege.Add(eintrag);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NeuerContext())
        {
            var gelesen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);

            Assert.Equal("Erdgeschoss links", gelesen.Name);
            Assert.Equal(812.50m, gelesen.Betrag);
            Assert.Equal(Startzeit, gelesen.ErstelltAm);
            Assert.Equal("test", gelesen.ErstelltVon);
            Assert.Null(gelesen.GeaendertAm);
            Assert.NotEqual(0u, gelesen.Version);
        }
    }

    [Fact]
    public void Id_ist_zeitlich_sortierbare_Guid_v7()
    {
        var eintrag = new TestEintrag { Name = "x" };

        Assert.Equal(7, eintrag.Id.Version);
    }

    [Fact]
    public async Task Aenderung_setzt_GeaendertAm_und_GeaendertVon()
    {
        var eintrag = new TestEintrag { Name = "alt" };
        await using (var db = NeuerContext())
        {
            db.Eintraege.Add(eintrag);
            await db.SaveChangesAsync(Ct);
        }

        Uhr.Advance(TimeSpan.FromHours(2));
        Benutzer.Kennung = "partnerin";
        await using (var db = NeuerContext())
        {
            var geladen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
            geladen.Name = "neu";
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NeuerContext())
        {
            var gelesen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
            Assert.Equal(Startzeit.AddHours(2), gelesen.GeaendertAm);
            Assert.Equal("partnerin", gelesen.GeaendertVon);
            Assert.Equal(Startzeit, gelesen.ErstelltAm);
            Assert.Equal("test", gelesen.ErstelltVon);
        }
    }

    [Fact]
    public async Task Loeschen_ist_logisch_und_blendet_den_Datensatz_aus()
    {
        var eintrag = new TestEintrag { Name = "weg" };
        await using (var db = NeuerContext())
        {
            db.Eintraege.Add(eintrag);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NeuerContext())
        {
            var geladen = await db.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
            db.Eintraege.Remove(geladen);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NeuerContext())
        {
            Assert.False(await db.Eintraege.AnyAsync(e => e.Id == eintrag.Id, Ct));

            var trotzdemVorhanden = await db.Eintraege.IgnoreQueryFilters().SingleAsync(e => e.Id == eintrag.Id, Ct);
            Assert.True(trotzdemVorhanden.Geloescht);
            Assert.Equal(Startzeit, trotzdemVorhanden.GeaendertAm);
        }
    }

    [Fact]
    public async Task Gleichzeitige_Aenderung_wird_als_Konflikt_erkannt()
    {
        var eintrag = new TestEintrag { Name = "start" };
        await using (var db = NeuerContext())
        {
            db.Eintraege.Add(eintrag);
            await db.SaveChangesAsync(Ct);
        }

        await using var erster = NeuerContext();
        await using var zweiter = NeuerContext();
        var ausErstem = await erster.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);
        var ausZweitem = await zweiter.Eintraege.SingleAsync(e => e.Id == eintrag.Id, Ct);

        ausErstem.Name = "erster";
        await erster.SaveChangesAsync(Ct);

        ausZweitem.Name = "zweiter";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => zweiter.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Geldbetraege_werden_als_numeric_18_2_gespeichert()
    {
        await using var db = NeuerContext();

        var typ = await db.Database
            .SqlQuery<string>($"SELECT format_type(atttypid, atttypmod) AS \"Value\" FROM pg_attribute WHERE attrelid = 'test_eintraege'::regclass AND attname = 'betrag'")
            .SingleAsync(Ct);

        Assert.Equal("numeric(18,2)", typ);
    }

    [Fact]
    public async Task Namen_werden_deutsch_sortiert()
    {
        await using (var db = NeuerContext())
        {
            db.Eintraege.AddRange(
                new TestEintrag { Name = "Zander" },
                new TestEintrag { Name = "Öztürk" },
                new TestEintrag { Name = "Ahrens" },
                new TestEintrag { Name = "Ohm" },
                new TestEintrag { Name = "Ärmel" });
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NeuerContext())
        {
            var namen = await db.Eintraege.OrderBy(e => e.Name).Select(e => e.Name).ToListAsync(Ct);

            Assert.Equal(["Ahrens", "Ärmel", "Ohm", "Öztürk", "Zander"], namen);
        }
    }
}
