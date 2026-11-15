using Elocaro.Domain.Properties;

namespace Elocaro.Domain.Tests.Properties;

public class SelfPaidCostTypesTests
{
    [Fact]
    public void Keeps_allocable_types_once_and_drops_heating_management_and_unknown_keys()
    {
        var result = SelfPaidCostTypes.Normalize(["betrkv-08", "betrkv-08", "betrkv-04", "verwaltung", "unbekannt"]);

        Assert.Equal(["betrkv-08"], result);
    }
}
