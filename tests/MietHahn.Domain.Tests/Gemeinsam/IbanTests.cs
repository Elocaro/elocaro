using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Domain.Tests.Gemeinsam;

public class IbanTests
{
    [Theory]
    [InlineData("DE02120300000000202051")]
    [InlineData("de02 1203 0000 0000 2020 51")]
    [InlineData("DE89370400440532013000")]
    [InlineData("AT611904300234573201")]
    [InlineData("NL91ABNA0417164300")]
    public void Gueltige_IBANs_werden_erkannt(string iban) => Assert.True(Iban.IstGueltig(iban));

    [Theory]
    [InlineData("DE02120300000000202052")] // Prüfziffer falsch
    [InlineData("DE0212030000000020205")] // zu kurz für DE
    [InlineData("1234567890")]
    [InlineData("DE02-1203-0000-0000-2020-51")]
    [InlineData("")]
    [InlineData(null)]
    public void Ungueltige_IBANs_werden_abgelehnt(string? iban) => Assert.False(Iban.IstGueltig(iban));

    [Fact]
    public void Formatierung_in_Vierergruppen() => Assert.Equal("DE02 1203 0000 0000 2020 51", Iban.Formatieren(" de02120300000000202051 "));
}
