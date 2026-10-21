using MietHahn.Infrastructure.Bank.FinTs;

namespace MietHahn.Integration.Tests.Bank;

public sealed class FinTsOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Product_number_defaults_to_the_registration_of_MietHahn(string? configured)
    {
        var options = new FinTsOptions { ProductId = configured };

        Assert.Equal(FinTsOptions.DefaultProductId, options.EffectiveProductId);
    }

    [Fact]
    public void Configured_product_number_overrides_the_default()
    {
        var options = new FinTsOptions { ProductId = " FORK123 " };

        Assert.Equal("FORK123", options.EffectiveProductId);
    }
}
