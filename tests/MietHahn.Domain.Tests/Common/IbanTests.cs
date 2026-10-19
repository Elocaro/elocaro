using MietHahn.Domain.Common;

namespace MietHahn.Domain.Tests.Common;

public class IbanTests
{
    [Theory]
    [InlineData("DE02120300000000202051")]
    [InlineData("de02 1203 0000 0000 2020 51")]
    [InlineData("DE89370400440532013000")]
    [InlineData("AT611904300234573201")]
    [InlineData("NL91ABNA0417164300")]
    public void Valid_IBANs_are_recognized(string iban) => Assert.True(Iban.IsValid(iban));

    [Theory]
    [InlineData("DE02120300000000202052")] // wrong check digits
    [InlineData("DE0212030000000020205")] // too short for DE
    [InlineData("1234567890")]
    [InlineData("DE02-1203-0000-0000-2020-51")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_IBANs_are_rejected(string? iban) => Assert.False(Iban.IsValid(iban));

    [Fact]
    public void Formatting_in_groups_of_four() => Assert.Equal("DE02 1203 0000 0000 2020 51", Iban.Format(" de02120300000000202051 "));
}
