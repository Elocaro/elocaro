using System.Globalization;
using Elocaro.Domain.Common;

namespace Elocaro.Domain.Tests.Common;

public class AppCultureTests
{
    [Fact]
    public async Task Texts_are_German_even_when_the_system_has_no_language()
    {
        // A container or build server has no system language: the default culture is the neutral one.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            AppCulture.Apply();

            // A thread of its own, as a background job or a request would run on.
            var text = await Task.Factory.StartNew(
                () => $"{1950m:0.00} € am {new DateTime(2026, 3, 1):d}",
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Assert.Equal("1950,00 € am 01.03.2026", text);
        }
        finally
        {
            AppCulture.Apply();
        }
    }
}
