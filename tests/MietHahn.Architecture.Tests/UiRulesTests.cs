using System.Text.RegularExpressions;

namespace MietHahn.Architecture.Tests;

/// <summary>
/// Guards the UI rules from docs/ui.md: pages use the building blocks instead of hand-written markup.
/// </summary>
public class UiRulesTests
{
    private static readonly string Components = Path.Combine(RepositoryRoot(), "src", "MietHahn.Web", "Components");

    [Fact]
    public void Messages_use_the_Notice_component()
    {
        AssertNoMatch(@"class=""message[ ""-]", "Use <Notice Level=\"…\"> instead of a hand-written message box", except: "Common/Notice.razor");
    }

    [Fact]
    public void Dates_are_formatted_through_Format()
    {
        AssertNoMatch(@"ToString\(""(dd\.MM\.yyyy|MMMM yyyy)""", "Use Format.Date/Format.Day/Format.Month");
    }

    [Fact]
    public void Empty_sections_use_EmptyState()
    {
        AssertNoMatch(@"<p(?: class=""small"")?>\s*(Noch keine|Keine Einträge)", "Use <EmptyState Compact=\"true\" Text=\"…\" />");
    }

    [Fact]
    public void Status_labels_use_StatusChip()
    {
        AssertNoMatch(@"class=""status[ ""-]", "Use <StatusChip Level=\"…\"> instead of a hand-written status span", except: "Common/StatusChip.razor");
    }

    [Fact]
    public void Layouts_use_BrandMark_and_SkipLink()
    {
        AssertNoMatch(@"class=""(skip-link|brand|account-brand)""", "Use <BrandMark> and <SkipLink> in layouts", except: "Common/BrandMark.razor", exceptAlso: "Common/SkipLink.razor");
    }

    [Fact]
    public void Filters_use_FilterBar()
    {
        AssertNoMatch(@"<form method=""get""", "Use <FilterBar> for filter and search forms", except: "Common/FilterBar.razor");
    }

    [Fact]
    public void Destructive_actions_ask_for_confirmation()
    {
        AssertNoMatch(@"button-danger", "Use <DangerAction> or <RowConfirm> instead of a bare danger button", except: "Common/DangerAction.razor", exceptAlso: "Common/RowConfirm.razor");
    }

    [Fact]
    public void Colors_come_from_tokens_not_from_inline_styles()
    {
        AssertNoMatch(@"style=""[^""]*#[0-9a-fA-F]{3,8}", "Use the --md-* color roles in app.css");
    }

    private static void AssertNoMatch(string pattern, string advice, string? except = null, string? exceptAlso = null)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(Components, "*.razor", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, except) && !IsExcluded(f, exceptAlso))
            .SelectMany(f => File.ReadLines(f).Select((line, index) => (f, line, number: index + 1)))
            .Where(x => regex.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(Components, x.f)}:{x.number}")
            .ToList();

        Assert.True(violations.Count == 0, $"{advice} (docs/ui.md):\n{string.Join("\n", violations)}");
    }

    private static bool IsExcluded(string path, string? except) =>
        except is not null && path.Replace('\\', '/').EndsWith(except, StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "MietHahn.Web")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
