using MietHahn.TestHelpers;

namespace MietHahn.Web.Tests.Browser;

/// <summary>Part 1 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart1(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 0);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 0);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 0);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 0);
}

/// <summary>Part 2 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart2(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 1);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 1);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 1);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 1);
}

/// <summary>Part 3 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart3(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 2);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 2);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 2);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 2);
}

/// <summary>Part 4 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart4(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 3);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 3);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 3);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 3);
}

/// <summary>Part 5 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart5(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 4);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 4);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 4);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 4);
}

/// <summary>Part 6 of 6 of the page sweeps (desktop and 360 px, contrast in light and dark).</summary>
public class PageSweepPart6(PostgresFixture postgres) : PageSweepBase(postgres)
{
    [Fact]
    public Task Every_page_loads_on_desktop() => EveryPageLoadsAsync(false, 5);

    [Fact]
    public Task Every_page_loads_at_360_pixels() => EveryPageLoadsAsync(true, 5);

    [Fact]
    public Task Contrast_in_light() => PagesMeetContrastAsync(false, 5);

    [Fact]
    public Task Contrast_in_dark() => PagesMeetContrastAsync(true, 5);
}
