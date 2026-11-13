using Elocaro.Application.Operations;
using Elocaro.Web.Components.Common;

namespace Elocaro.Web.Tests;

public class DeviceLinkTests
{
    [Fact]
    public void The_code_is_appended_as_query_parameter()
    {
        var link = BackupTargetFields.DeviceLink(new DeviceAuthorization("d", "ABC-DEF-GHIJ", "https://www.google.com/device", 600, 5));

        Assert.Equal("https://www.google.com/device?user_code=ABC-DEF-GHIJ", link);
    }

    [Fact]
    public void An_existing_query_is_continued_and_the_code_is_escaped()
    {
        var link = BackupTargetFields.DeviceLink(new DeviceAuthorization("d", "A B&C", "https://example.test/device?hl=de", 600, 5));

        Assert.Equal("https://example.test/device?hl=de&user_code=A%20B%26C", link);
    }
}
