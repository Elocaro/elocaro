namespace Elocaro.Architecture.Tests;

/// <summary>
/// The <c>@code</c> block of a page or component holds what the markup needs, not the workflow. A long block collects flow control
/// that no test reaches without a browser; it belongs into the service behind the page or into a component of its own. The blocks that
/// are longer today are frozen with their size: they must not grow, and a block that got below the limit leaves the list.
/// </summary>
public class PageSizeRulesTests
{
    private const int Limit = 200;

    /// <summary>Lines of the <c>@code</c> block (from the directive to the end of the file) of the files above the limit today. Goal: empty.</summary>
    private static readonly Dictionary<string, int> KnownLargeBlocks = new(StringComparer.Ordinal)
    {
        ["Pages/Admin/Operations/Backup.razor"] = 529,
        ["Pages/Admin/Leases/HandoverPage.razor"] = 324,
        ["Pages/Admin/Settings/SetupWizard.razor"] = 290,
        ["Pages/Admin/Finance/TenantAccountPage.razor"] = 256,
        ["Account/Login/RestoreSetup.razor"] = 249,
        ["Pages/Admin/Leases/Details.razor"] = 224,
        ["Pages/Admin/OperatingCosts/StatementWizardPage.razor"] = 219,
        ["Common/BackupTargetFields.razor"] = 211,
        ["Pages/Admin/Leases/Create.razor"] = 210,
        ["Pages/Admin/Leases/RentIncreasePage.razor"] = 204,
        ["Pages/Admin/Operations/BackupTargetDialog.razor"] = 201,
    };

    [Fact]
    public void Code_blocks_stay_below_the_limit_and_the_known_ones_do_not_grow()
    {
        var sizes = CodeBlockSizes();

        var tooLong = sizes.Where(s => s.Value > Limit && !KnownLargeBlocks.ContainsKey(s.Key)).Select(s => $"{s.Key}: {s.Value} lines").ToList();
        var grown = sizes.Where(s => KnownLargeBlocks.TryGetValue(s.Key, out var frozen) && s.Value > frozen).Select(s => $"{s.Key}: {s.Value} lines (frozen at {KnownLargeBlocks[s.Key]})").ToList();
        Assert.True(tooLong.Count == 0, $"@code block longer than {Limit} lines: move the flow into the service or split the page into components (docs/architektur.md):\n" + string.Join("\n", tooLong));
        Assert.True(grown.Count == 0, "A frozen @code block has grown; it may only shrink (lower the number when it does):\n" + string.Join("\n", grown));
    }

    [Fact]
    public void Blocks_that_got_below_the_limit_leave_the_list()
    {
        var sizes = CodeBlockSizes();
        var resolved = KnownLargeBlocks.Keys.Where(k => !sizes.TryGetValue(k, out var size) || size <= Limit).ToList();
        Assert.True(resolved.Count == 0, "Below the limit now (or gone), remove from KnownLargeBlocks:\n" + string.Join("\n", resolved));
    }

    private static Dictionary<string, int> CodeBlockSizes() =>
        Directory.EnumerateFiles(Repository.Components, "*.razor", SearchOption.AllDirectories)
            .Where(f => !Repository.IsBuildOutput(f))
            .ToDictionary(f => Repository.Relative(Repository.Components, f), CodeBlockLines, StringComparer.Ordinal);

    private static int CodeBlockLines(string file)
    {
        var lines = File.ReadAllLines(file);
        var start = Array.FindIndex(lines, l => l.StartsWith("@code", StringComparison.Ordinal));
        return start < 0 ? 0 : lines.Length - start;
    }
}
