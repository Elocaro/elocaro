using MietHahn.Domain.Dateien;

namespace MietHahn.Domain.Tests.Dateien;

public class DateiTests
{
    private const string Sha = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";

    [Fact]
    public void Hash_wird_in_Kleinbuchstaben_gespeichert()
    {
        var datei = new Datei("beleg.pdf", "application/pdf", 10, Sha);

        Assert.Equal(Sha.ToLowerInvariant(), datei.Sha256);
        Assert.False(datei.IstBild);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Ungueltiger_Hash_wird_abgelehnt(string sha)
    {
        Assert.Throws<ArgumentException>(() => new Datei("x.pdf", "application/pdf", 1, sha));
    }

    [Fact]
    public void Zu_langer_Name_wird_gekuerzt()
    {
        var datei = new Datei(new string('a', 300) + ".jpg", "image/jpeg", 1, Sha);

        Assert.Equal(Datei.MaxNameLaenge, datei.Name.Length);
        Assert.True(datei.IstBild);
    }

    [Fact]
    public void Leere_Datei_wird_abgelehnt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Datei("x.pdf", "application/pdf", 0, Sha));
    }
}
