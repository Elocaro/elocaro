namespace Elocaro.Architecture.Tests;

/// <summary>
/// The <c>@code</c> block of a page or component holds what the markup needs, not the workflow. A long block collects flow control
/// that no test reaches without a browser; it belongs into the service behind the page, into a class next to the page or into a
/// component of its own.
/// </summary>
public class PageSizeRulesTests
{
    private const int Limit = 200;

    [Fact]
    public void Code_blocks_stay_below_the_limit()
    {
        var tooLong = Directory.EnumerateFiles(Repository.Components, "*.razor", SearchOption.AllDirectories)
            .Where(f => !Repository.IsBuildOutput(f))
            .Select(f => (File: Repository.Relative(Repository.Components, f), Lines: CodeBlockLines(f)))
            .Where(s => s.Lines > Limit)
            .Select(s => $"{s.File}: {s.Lines} lines")
            .ToList();

        Assert.True(tooLong.Count == 0, $"@code block longer than {Limit} lines: move the flow into the service, a class next to the page or a component (docs/architektur.md):\n" + string.Join("\n", tooLong));
    }

    private static int CodeBlockLines(string file)
    {
        var lines = File.ReadAllLines(file);
        var start = Array.FindIndex(lines, l => l.StartsWith("@code", StringComparison.Ordinal));
        return start < 0 ? 0 : lines.Length - start;
    }
}
