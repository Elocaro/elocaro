using MietHahn.Application.Admin;

namespace MietHahn.Integration.Tests.Admin;

public class ModuleCatalogTests
{
    [Fact]
    public void Every_module_is_described_exactly_once()
    {
        Assert.Equal(Enum.GetValues<AppModule>().Order(), ModuleCatalog.All.Select(m => m.Module).Order());
        Assert.All(ModuleCatalog.All, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Title));
            Assert.False(string.IsNullOrWhiteSpace(m.Summary));
            Assert.False(string.IsNullOrWhiteSpace(m.Purpose));
            Assert.NotEmpty(m.Includes);
        });
    }

    [Fact]
    public void Required_modules_never_depend_on_themselves()
    {
        foreach (var info in ModuleCatalog.All)
        {
            var reached = ModuleCatalog.WithRequirements(info.Requires);
            Assert.DoesNotContain(info.Module, reached);
        }
    }

    [Fact]
    public void Switching_on_a_module_switches_on_what_it_requires()
    {
        var selection = ModuleCatalog.WithRequirements([AppModule.OperatingCosts]);

        Assert.Contains(AppModule.Receipts, selection);
    }

    [Theory]
    [InlineData("/verwaltung/vpi", AppModule.RentIncreases)]
    [InlineData("/verwaltung/bank/konten/neu", AppModule.Bank)]
    [InlineData("/portal/verbrauch", AppModule.Metering)]
    [InlineData("/verwaltung/objekte/0c6a4e4e-6f5b-4b9b-8f43-0d4e2f1f3a11/heizung", AppModule.OperatingCosts)]
    [InlineData("/verwaltung/vertraege/0c6a4e4e-6f5b-4b9b-8f43-0d4e2f1f3a11/mieterhoehung", AppModule.RentIncreases)]
    public void Pages_belong_to_their_module(string path, AppModule module) =>
        Assert.Equal(module, ModuleCatalog.OwnerOf(path)?.Module);

    [Theory]
    [InlineData("/verwaltung")]
    [InlineData("/verwaltung/objekte")]
    [InlineData("/verwaltung/objekte/0c6a4e4e-6f5b-4b9b-8f43-0d4e2f1f3a11")]
    [InlineData("/verwaltung/vertraege/0c6a4e4e-6f5b-4b9b-8f43-0d4e2f1f3a11/konto")]
    [InlineData("/verwaltung/bankfremd")]
    public void Core_pages_belong_to_no_module(string path) => Assert.Null(ModuleCatalog.OwnerOf(path));
}
