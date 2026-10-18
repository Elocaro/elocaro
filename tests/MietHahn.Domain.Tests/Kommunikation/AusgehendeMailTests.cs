using MietHahn.Domain.Kommunikation;

namespace MietHahn.Domain.Tests.Kommunikation;

public class AusgehendeMailTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static AusgehendeMail NeueMail(params string[] an) =>
        new(an.Length == 0 ? ["mieter@example.de"] : an, "Betreff", "<p>Hallo</p>", "Hallo", Jetzt);

    [Fact]
    public void Neue_Mail_ist_sofort_faellig()
    {
        var mail = NeueMail();

        Assert.Equal(MailStatus.Ausstehend, mail.Status);
        Assert.Equal(Jetzt, mail.NaechsterVersuchAm);
        Assert.Equal(0, mail.Versuche);
    }

    [Fact]
    public void Empfaenger_werden_bereinigt_und_dedupliziert()
    {
        var mail = NeueMail(" a@example.de ", "A@example.de", "", "b@example.de");

        Assert.Equal(["a@example.de", "b@example.de"], mail.An);
    }

    [Fact]
    public void Ohne_Empfaenger_wird_abgelehnt()
    {
        Assert.Throws<ArgumentException>(() => NeueMail(" "));
    }

    [Fact]
    public void Ungueltige_Adresse_wird_abgelehnt()
    {
        var fehler = Assert.Throws<ArgumentException>(() => NeueMail("kein-at-zeichen"));
        Assert.Contains("kein-at-zeichen", fehler.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(9, 256)]
    [InlineData(10, 360)]
    [InlineData(30, 360)]
    public void Wartezeit_waechst_exponentiell_bis_sechs_Stunden(int fehlversuche, int erwarteteMinuten)
    {
        Assert.Equal(TimeSpan.FromMinutes(erwarteteMinuten), AusgehendeMail.Wartezeit(fehlversuche));
    }

    [Fact]
    public void Fehlversuch_plant_naechsten_Versuch_mit_Backoff()
    {
        var mail = NeueMail();

        mail.MarkiereFehlversuch(Jetzt, "SMTP nicht erreichbar");
        mail.MarkiereFehlversuch(Jetzt.AddMinutes(1), "SMTP nicht erreichbar");

        Assert.Equal(MailStatus.Ausstehend, mail.Status);
        Assert.Equal(2, mail.Versuche);
        Assert.Equal(Jetzt.AddMinutes(3), mail.NaechsterVersuchAm);
        Assert.Equal("SMTP nicht erreichbar", mail.LetzterFehler);
    }

    [Fact]
    public void Nach_maximalen_Versuchen_ist_die_Mail_fehlgeschlagen()
    {
        var mail = NeueMail();

        for (var i = 0; i < AusgehendeMail.MaxVersuche; i++)
        {
            mail.MarkiereFehlversuch(Jetzt, "Fehler");
        }

        Assert.Equal(MailStatus.Fehlgeschlagen, mail.Status);
        Assert.Equal(AusgehendeMail.MaxVersuche, mail.Versuche);
    }

    [Fact]
    public void Langer_Fehlertext_wird_gekuerzt()
    {
        var mail = NeueMail();

        mail.MarkiereFehlversuch(Jetzt, new string('x', 5000));

        Assert.Equal(AusgehendeMail.MaxFehlerLaenge, mail.LetzterFehler!.Length);
    }

    [Fact]
    public void Erfolgreicher_Versand_wird_vermerkt()
    {
        var mail = NeueMail();
        mail.MarkiereFehlversuch(Jetzt, "Fehler");

        mail.MarkiereGesendet(Jetzt.AddMinutes(1));

        Assert.Equal(MailStatus.Gesendet, mail.Status);
        Assert.Equal(Jetzt.AddMinutes(1), mail.GesendetAm);
        Assert.Null(mail.LetzterFehler);
    }

    [Fact]
    public void Fehlgeschlagene_Mail_kann_erneut_versucht_werden()
    {
        var mail = NeueMail();
        for (var i = 0; i < AusgehendeMail.MaxVersuche; i++)
        {
            mail.MarkiereFehlversuch(Jetzt, "Fehler");
        }

        mail.ErneutVersuchen(Jetzt.AddDays(1));

        Assert.Equal(MailStatus.Ausstehend, mail.Status);
        Assert.Equal(0, mail.Versuche);
        Assert.Equal(Jetzt.AddDays(1), mail.NaechsterVersuchAm);
    }

    [Fact]
    public void Gesendete_Mail_kann_nicht_erneut_versucht_werden()
    {
        var mail = NeueMail();
        mail.MarkiereGesendet(Jetzt);

        Assert.Throws<InvalidOperationException>(() => mail.ErneutVersuchen(Jetzt));
    }
}
