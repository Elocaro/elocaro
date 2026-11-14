using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// Guards the rules for interactive pages from docs/ui.md (section "Interaktive Seiten") and ADR 0005: a page is interactive when it
/// carries <c>@rendermode InteractiveServer</c>. The rules read the .razor files like <see cref="UiRulesTests"/>.
/// </summary>
public partial class InteractiveUiRulesTests
{
    /// <summary>Pages below Components/Account that are interactive today although the sign-in area stays static. Goal: empty.</summary>
    private static readonly string[] KnownInteractiveAccountPages =
    [
        "Account/Login/RestoreSetup.razor", // restore from a backup during the first setup; no cookie is set on this page
    ];

    /// <summary>The one MudBlazor type a component may take: the dialog service hands every dialog its own instance (cascading parameter), nobody sets it.</summary>
    private const string DialogInstance = "IMudDialogInstance";

    private static readonly IReadOnlyList<RazorFile> Files = Directory.EnumerateFiles(Repository.Components, "*.razor", SearchOption.AllDirectories)
        .Where(f => !Repository.IsBuildOutput(f))
        .Select(f => new RazorFile(Repository.Relative(Repository.Components, f), File.ReadAllText(f)))
        .ToList();

    private static IEnumerable<RazorFile> InteractivePages => Files.Where(f => f.IsPage && f.IsInteractive);

    [Fact]
    public void Interactive_pages_exist()
    {
        // Guards the guards: without pages every rule below would pass without checking anything.
        Assert.True(InteractivePages.Count() > 50, "No interactive pages found; the page detection in this test class no longer matches the code.");
    }

    [Fact]
    public void Interactive_pages_wrap_their_content_in_InteractiveShell()
    {
        var violations = InteractivePages.Where(f => !f.Text.Contains("<InteractiveShell", StringComparison.Ordinal)).Select(f => f.Path).ToList();
        AssertNone(violations, "An interactive page wraps its content in <InteractiveShell> (providers, light/dark, marker data-interactive) (docs/ui.md, Interaktive Seiten)");
    }

    [Fact]
    public void MudBlazor_is_imported_in_the_page_not_globally()
    {
        var violations = Files.Where(f => f.Path.EndsWith("_Imports.razor", StringComparison.Ordinal) && f.Text.Contains("MudBlazor", StringComparison.Ordinal)).Select(f => f.Path).ToList();
        AssertNone(violations, "Put @using MudBlazor into the interactive page, not into an _Imports.razor, so that static pages cannot reach for it (docs/ui.md, Interaktive Seiten)");
    }

    [Fact]
    public void Interactive_pages_give_feedback_through_Notifier_not_the_status_cookie()
    {
        var violations = InteractivePages
            .Where(f => f.Text.Contains("<StatusMessage", StringComparison.Ordinal) || f.Text.Contains("Redirector", StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();
        AssertNone(violations, "Use Notifier.Success/Info/Warning/Error; after navigating, pass ?meldung=<key> (StatusMessage.Keys), which InteractiveShell shows as a snackbar. No <StatusMessage> and no Redirector cookie (docs/ui.md, Interaktive Seiten)");
    }

    [Fact]
    public void Interactive_forms_use_OnValidSubmit_without_form_binding()
    {
        var violations = InteractivePages.Where(f => FormBinding().IsMatch(f.Text)).Select(f => f.Path).ToList();
        AssertNone(violations, "Interactive forms are an EditForm with OnValidSubmit; [SupplyParameterFromForm] and @formname belong to static pages (ADR 0005)");
    }

    [Fact]
    public void Interactive_pages_have_no_return_in_the_markup()
    {
        var violations = InteractivePages
            .SelectMany(f => f.Markup.Split('\n').Select((line, index) => (f.Path, line, number: index + 1)))
            .Where(x => ReturnStatement().IsMatch(x.line))
            .Select(x => $"{x.Path}:{x.number}")
            .ToList();
        AssertNone(violations, "No return; in the markup of an interactive page: write loading, not-found and content as if / else if / else (docs/ui.md, Interaktive Seiten)");
    }

    [Fact]
    public void Sign_in_and_account_pages_stay_static_and_resolved_exceptions_leave_the_list()
    {
        var interactive = Files.Where(f => f.IsPage && f.IsInteractive && f.Path.StartsWith("Account/", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal).ToList();

        var added = interactive.Except(KnownInteractiveAccountPages).ToList();
        var resolved = KnownInteractiveAccountPages.Except(interactive).ToList();
        AssertNone(added, "Sign-in, magic link, sign-out and account pages stay static, because a circuit cannot set cookies (ADR 0005)");
        AssertNone(resolved, "Page is static now, remove it from KnownInteractiveAccountPages");
    }

    [Fact]
    public void Own_components_expose_no_MudBlazor_types_in_their_parameters()
    {
        var violations = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IComponent).IsAssignableFrom(t) && !t.IsDefined(typeof(RouteAttribute), inherit: false))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => (p.IsDefined(typeof(ParameterAttribute)) || p.IsDefined(typeof(CascadingParameterAttribute))) && p.PropertyType.Name != DialogInstance && UsesMudBlazor(p.PropertyType))
                .Select(p => $"{t.FullName}.{p.Name}: {p.PropertyType.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        AssertNone(violations, "Own components take simple parameters and EventCallbacks, no MudBlazor types, so that they can be replaced (docs/ui.md, Interaktive Seiten)");
    }

    private static bool UsesMudBlazor(Type type) =>
        type.Assembly.GetName().Name == "MudBlazor" || (type.HasElementType && UsesMudBlazor(type.GetElementType()!)) || type.GetGenericArguments().Any(UsesMudBlazor);

    private static void AssertNone(List<string> violations, string advice) =>
        Assert.True(violations.Count == 0, $"{advice}:\n{string.Join("\n", violations)}");

    [GeneratedRegex(@"SupplyParameterFromForm|@formname", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormBinding();

    [GeneratedRegex(@"\breturn\s*;", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReturnStatement();

    private sealed partial record RazorFile(string Path, string Text)
    {
        public bool IsPage => PageDirective().IsMatch(Text);

        public bool IsInteractive => Text.Contains("@rendermode InteractiveServer", StringComparison.Ordinal);

        /// <summary>Everything before the first <c>@code</c> block.</summary>
        public string Markup => Text.IndexOf("@code", StringComparison.Ordinal) is var code and >= 0 ? Text[..code] : Text;

        [GeneratedRegex(@"^@page\s", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
        private static partial Regex PageDirective();
    }
}
