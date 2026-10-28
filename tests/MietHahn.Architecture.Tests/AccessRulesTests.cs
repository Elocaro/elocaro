using System.Text.RegularExpressions;

namespace MietHahn.Architecture.Tests;

/// <summary>
/// Access control as a rule of the code base (docs/architektur.md): every routable page states who may open it, either through
/// its own attribute or through the <c>_Imports.razor</c> of its folder. The fallback policy (authenticated users) is only the net
/// below; it must not be what protects an administration page.
/// </summary>
public class AccessRulesTests
{
    /// <summary>Pages that are reachable without signing in, on purpose (sign-in, public links with a token, errors).</summary>
    private static readonly string[] AnonymousPages =
    [
        "Account/Login/",
        "Pages/About.razor",
        "Pages/Contractor/ContractorTicketPage.razor",
        "Pages/Error.razor",
        "Pages/NotFound.razor",
        "Pages/Signing.razor",
    ];

    /// <summary>Policy each area has to demand (the area of a page is its folder below Pages).</summary>
    private static readonly (string Folder, string Policy)[] AreaPolicies =
    [
        ("Pages/Admin/", "Policies."),
        ("Pages/Owner/", "Policies.Owner"),
        ("Pages/Portal/", "Policies.Portal"),
        ("Pages/Service/", "Policies.Service"),
    ];

    [Fact]
    public void Every_page_states_its_access_explicitly()
    {
        var violations = Pages().Where(p => !Attributes(p).Contains("[Authorize", StringComparison.Ordinal) && !Attributes(p).Contains("[AllowAnonymous]", StringComparison.Ordinal)).Select(Name).ToList();

        Assert.True(violations.Count == 0, "Add [Authorize(Policy = …)] or [AllowAnonymous] (own attribute or _Imports.razor of the folder):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Only_the_listed_pages_are_open_to_anonymous_users()
    {
        var open = Pages().Where(p => Attributes(p).Contains("[AllowAnonymous]", StringComparison.Ordinal)).Select(Name)
            .Where(name => !AnonymousPages.Any(allowed => name.StartsWith(allowed, StringComparison.Ordinal)))
            .ToList();

        Assert.True(open.Count == 0, "A new anonymous page needs a decision: add it to AnonymousPages with a reason, or protect it.\n" + string.Join("\n", open));
    }

    [Fact]
    public void Each_area_demands_its_own_policy()
    {
        var violations = new List<string>();
        foreach (var (folder, policy) in AreaPolicies)
        {
            violations.AddRange(Pages().Where(p => Name(p).StartsWith(folder, StringComparison.Ordinal) && !Attributes(p).Contains(policy, StringComparison.Ordinal))
                .Select(p => $"{Name(p)} (expects {policy})"));
        }

        Assert.True(violations.Count == 0, "A page of an area must require the policy of that area:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Web_reaches_Infrastructure_only_where_identity_and_start_up_need_it()
    {
        // Baseline: identity (ApplicationUser) and start-up. The list may shrink, it must not grow: pages talk to Application interfaces.
        string[] allowed =
        [
            "Program.cs",
            "Account/AccountExtensions.cs",
            "Account/RevalidatingAuthenticationStateProvider.cs",
            "Operations/OperationsExtensions.cs",
            "Components/Account/_Imports.razor",
            "Components/Layout/UserMenu.razor",
            "Components/Pages/Admin/Users/Details.razor",
        ];
        var users = Directory.EnumerateFiles(Repository.Web, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)) && !IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains("using MietHahn.Infrastructure", StringComparison.Ordinal))
            .Select(f => Repository.Relative(Repository.Web, f))
            .ToList();

        var added = users.Except(allowed).ToList();
        var gone = allowed.Except(users).ToList();
        Assert.True(added.Count == 0, "Web code must use Application interfaces, not Infrastructure types:\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "No longer uses Infrastructure, remove from the baseline:\n" + string.Join("\n", gone));
    }

    private static IEnumerable<string> Pages() =>
        Directory.EnumerateFiles(Repository.Components, "*.razor", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f) && Regex.IsMatch(File.ReadAllText(f), @"^@page\s", RegexOptions.Multiline));

    private static string Name(string file) => Repository.Relative(Repository.Components, file);

    /// <summary>The attribute lines of the page itself plus the _Imports.razor of its folder and every folder above (up to Components).</summary>
    private static string Attributes(string file)
    {
        var text = File.ReadAllText(file);
        for (var directory = Path.GetDirectoryName(file); directory is not null && directory.Length >= Repository.Components.Length; directory = Path.GetDirectoryName(directory))
        {
            var imports = Path.Combine(directory, "_Imports.razor");
            if (File.Exists(imports))
            {
                text += "\n" + File.ReadAllText(imports);
            }
        }

        return string.Join("\n", text.Split('\n').Where(line => line.TrimStart().StartsWith("@attribute", StringComparison.Ordinal)));
    }

    private static bool IsBuildOutput(string path) => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
