using Microsoft.EntityFrameworkCore;
using MietHahn.Domain.Gemeinsam;
using MietHahn.Infrastructure.Persistenz;
using MietHahn.Infrastructure.Persistenz.MigrationsHilfen;
using MietHahn.Integration.Tests.Infrastruktur;
using Npgsql;

namespace MietHahn.Integration.Tests.Persistenz;

public class UeberlappungsschutzTests(PostgresFixture postgres) : PersistenzTestBasis(postgres)
{
    private static readonly Guid Wohnung1 = Guid.CreateVersion7();
    private static readonly Guid Wohnung2 = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = NeuerContext();
        var sql = UeberlappungsschutzExtensions.ErstellenSql("ex_test_eintraege_gruppe_zeitraum", "test_eintraege", "gueltigkeit_von", "gueltigkeit_bis", "gruppe_id");
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
    }

    [Fact]
    public void Sql_quotet_Bezeichner_und_ignoriert_geloeschte_Zeilen()
    {
        var sql = UeberlappungsschutzExtensions.ErstellenSql("ex", "mietvertraege", "beginn", "ende", "mietobjekt_id");

        Assert.Equal(
            "ALTER TABLE \"mietvertraege\" ADD CONSTRAINT \"ex\" EXCLUDE USING gist " +
            "(\"mietobjekt_id\" WITH =, daterange(\"beginn\", \"ende\", '[]') WITH &&) WHERE (NOT geloescht) DEFERRABLE INITIALLY DEFERRED;",
            sql);
    }

    [Fact]
    public async Task Nicht_ueberlappende_Zeitraeume_sind_erlaubt()
    {
        await SpeichereAsync(
            Eintrag(Wohnung1, new(2025, 1, 1), new(2025, 12, 31)),
            Eintrag(Wohnung1, new(2026, 1, 1), null));
    }

    [Fact]
    public async Task Ueberlappende_Zeitraeume_derselben_Gruppe_werden_abgelehnt()
    {
        await SpeichereAsync(Eintrag(Wohnung1, new(2025, 1, 1), new(2025, 12, 31)));

        var fehler = await Assert.ThrowsAnyAsync<Exception>(
            () => SpeichereAsync(Eintrag(Wohnung1, new(2025, 12, 31), null)));

        Assert.True(Datenbankfehler.IstUeberlappung(fehler), fehler.ToString());
    }

    [Fact]
    public async Task Unbefristeter_Zeitraum_blockiert_spaetere_Eintraege()
    {
        await SpeichereAsync(Eintrag(Wohnung1, new(2025, 1, 1), null));

        var fehler = await Assert.ThrowsAnyAsync<Exception>(
            () => SpeichereAsync(Eintrag(Wohnung1, new(2030, 1, 1), new(2030, 6, 30))));

        Assert.True(Datenbankfehler.IstUeberlappung(fehler));
    }

    [Fact]
    public async Task Verschiedene_Gruppen_duerfen_sich_ueberlappen()
    {
        await SpeichereAsync(
            Eintrag(Wohnung1, new(2025, 1, 1), null),
            Eintrag(Wohnung2, new(2025, 1, 1), null));
    }

    [Fact]
    public async Task Logisch_geloeschte_Eintraege_blockieren_nicht()
    {
        var alt = Eintrag(Wohnung1, new(2025, 1, 1), null);
        await SpeichereAsync(alt);

        await using (var db = NeuerContext())
        {
            db.Eintraege.Remove(await db.Eintraege.SingleAsync(e => e.Id == alt.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await SpeichereAsync(Eintrag(Wohnung1, new(2025, 6, 1), null));
    }

    [Fact]
    public async Task Fortschreiben_in_einem_Speichervorgang_ist_erlaubt()
    {
        var alt = Eintrag(Wohnung1, new(2025, 1, 1), null);
        await SpeichereAsync(alt);

        // Neuer Stand und Beenden des alten in einem SaveChanges: die Prüfung erfolgt erst beim Commit.
        await using var db = NeuerContext();
        var geladen = await db.Eintraege.SingleAsync(e => e.Id == alt.Id, Ct);
        db.Eintraege.Add(Eintrag(Wohnung1, new(2026, 1, 1), null));
        geladen.Gueltigkeit = new Zeitraum(new(2025, 1, 1), new(2025, 12, 31));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(2, await db.Eintraege.CountAsync(Ct));
    }

    [Fact]
    public void Andere_Datenbankfehler_sind_keine_Ueberlappung()
    {
        Assert.False(Datenbankfehler.IstUeberlappung(new InvalidOperationException()));
        Assert.False(Datenbankfehler.IstUeberlappung(new DbUpdateException("x", new InvalidOperationException())));
    }

    private static TestEintrag Eintrag(Guid gruppe, DateOnly von, DateOnly? bis) =>
        new() { Name = "Vertrag", GruppeId = gruppe, Gueltigkeit = new Zeitraum(von, bis) };

    private async Task SpeichereAsync(params TestEintrag[] eintraege)
    {
        await using var db = NeuerContext();
        db.Eintraege.AddRange(eintraege);
        await db.SaveChangesAsync(Ct);
    }
}
