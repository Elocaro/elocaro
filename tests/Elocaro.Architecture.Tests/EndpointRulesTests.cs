using System.Text.RegularExpressions;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// Minimal API endpoints (downloads, exports, uploads) are a second way to the services next to the pages, and the administration
/// services check no permissions themselves. Like a page, every endpoint therefore states who may call it: a policy, or
/// <c>AllowAnonymous</c> for the listed links with a token. Naming no policy (the fallback policy, or <c>RequireAuthorization()</c>
/// without an argument) is a decision that has to be written down here, because "signed in" includes tenants and tradespeople.
/// </summary>
public partial class EndpointRulesTests
{
    /// <summary>Endpoints for every signed-in user, whatever the role; they state no policy and the fallback policy protects them.</summary>
    private static readonly string[] SignedInIsEnough =
    [
        "AccessControl/FileAccess.cs: /dateien/{id:guid}", // the handler authorizes the file itself (FileAccessHandler)
        "Account/AccountExtensions.cs: /konto/abmelden",
        "Account/AccountExtensions.cs: /konto/darstellung",
        "Push/PushEndpoints.cs: /abmelden", // push notifications of the own account, for every role (group /push)
        "Push/PushEndpoints.cs: /abonnieren",
        "Push/PushEndpoints.cs: /ausstehend",
        "Push/PushEndpoints.cs: /schluessel",
        "Push/PushEndpoints.cs: /status",
    ];

    /// <summary>Endpoints that are reachable without signing in, on purpose (links with a token, restore before the first account, health).</summary>
    private static readonly string[] AnonymousEndpoints =
    [
        "Deadlines/CalendarEndpoint.cs: /kalender/{token}.ics",
        "Notices/ContractorFileEndpoint.cs: /auftrag/{token}/datei/{id:guid}",
        "Operations/BackupFileEndpoints.cs: /konto/einrichtung/wiederherstellen/hochladen",
        "Operations/OperationsExtensions.cs: /health",
        "Operations/OperationsExtensions.cs: /health/live",
        "Signatures/SignatureDocumentEndpoint.cs: /unterschrift/{token}/dokument",
    ];

    [Fact]
    public void There_are_endpoints()
    {
        Assert.True(Endpoints().Count > 20, "The endpoints were not found; the rules below would pass without checking anything.");
    }

    [Fact]
    public void Every_endpoint_states_its_access_or_is_listed_as_open_to_every_signed_in_user()
    {
        var silent = Endpoints().Where(e => e.Access == Access.SignedIn).Select(e => e.Name).Order(StringComparer.Ordinal).ToList();

        var added = silent.Except(SignedInIsEnough).ToList();
        var gone = SignedInIsEnough.Except(silent).ToList();
        Assert.True(added.Count == 0, "Add RequireAuthorization(Policies.…) to the endpoint; the fallback policy lets every signed-in user in, tenants included:\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "States its access now (or is gone), remove from SignedInIsEnough:\n" + string.Join("\n", gone));
    }

    [Fact]
    public void Only_the_listed_endpoints_are_open_to_anonymous_users()
    {
        var open = Endpoints().Where(e => e.Access == Access.Anonymous).Select(e => e.Name).Order(StringComparer.Ordinal).ToList();

        var added = open.Except(AnonymousEndpoints).ToList();
        var gone = AnonymousEndpoints.Except(open).ToList();
        Assert.True(added.Count == 0, "A new anonymous endpoint needs a decision: add it to AnonymousEndpoints with a reason, or protect it.\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "No longer anonymous (or gone), remove from AnonymousEndpoints:\n" + string.Join("\n", gone));
    }

    [Fact]
    public void Endpoints_below_an_area_demand_a_policy_of_that_area()
    {
        // The same areas as for the pages (AccessRulesTests): a download below /verwaltung must not be open to the portal roles.
        (string Prefix, string Policy)[] areas =
        [
            ("/verwaltung/", "Policies."),
            ("/eigentuemer/", "Policies.Owner"),
            ("/portal/", "Policies.Portal"),
            ("/service/", "Policies.Service"),
        ];
        var violations = Endpoints()
            .SelectMany(e => areas.Where(a => e.Route.StartsWith(a.Prefix, StringComparison.Ordinal) && !e.Statement.Contains($"RequireAuthorization({a.Policy}", StringComparison.Ordinal))
                .Select(a => $"{e.Name} (expects {a.Policy})"))
            .ToList();

        Assert.True(violations.Count == 0, "An endpoint of an area must require the policy of that area:\n" + string.Join("\n", violations));
    }

    private enum Access
    {
        /// <summary>No policy named: nothing stated (fallback policy) or a bare <c>RequireAuthorization()</c>; both let every signed-in user in.</summary>
        SignedIn,
        Policy,
        Anonymous,
    }

    private sealed record Endpoint(string Name, string Route, string Statement, Access Access);

    /// <summary>Every <c>Map…</c> call in the web project with its whole statement (up to the semicolon), so that the chained calls are part of it.</summary>
    private static List<Endpoint> Endpoints()
    {
        var result = new List<Endpoint>();
        foreach (var file in Directory.EnumerateFiles(Repository.Web, "*.cs", SearchOption.AllDirectories).Where(f => !Repository.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            // Groups that name a policy for all their endpoints: var group = endpoints.MapGroup("/x").RequireAuthorization(Policies.…);
            var protectedGroups = ProtectedGroup().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var constants = Constants(text);
            foreach (Match match in MapCall().Matches(text))
            {
                var statement = Statement(text, match.Index);
                var argument = match.Groups[2].Value.Trim();
                var route = constants.TryGetValue(argument, out var value) ? value : argument.Trim('"');
                var access = statement.Contains(".AllowAnonymous()", StringComparison.Ordinal) ? Access.Anonymous
                    : NamedPolicy().IsMatch(statement) || protectedGroups.Contains(match.Groups[1].Value) ? Access.Policy
                    : Access.SignedIn;
                result.Add(new Endpoint($"{Repository.Relative(Repository.Web, file)}: {route}", route, statement, access));
            }
        }

        return result;
    }

    /// <summary>String constants of a file by name, so that a route given as <c>DownloadPath</c> is checked like one written out (also <c>A + "/b"</c>).</summary>
    private static Dictionary<string, string> Constants(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Constant().Matches(text))
        {
            var prefix = match.Groups[2].Success && result.TryGetValue(match.Groups[2].Value, out var known) ? known : "";
            result[match.Groups[1].Value] = prefix + match.Groups[3].Value;
        }

        return result;
    }

    /// <summary>The statement starting at <paramref name="start"/>: up to the first semicolon outside of brackets, strings and comments.</summary>
    private static string Statement(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var lineEnd = text.IndexOf('\n', i);
                i = lineEnd >= 0 ? lineEnd : text.Length;
            }
            else if (c == '"')
            {
                i = EndOfString(text, i);
            }
            else if (c == '\'' && i + 2 < text.Length)
            {
                i += text[i + 1] == '\\' ? 3 : 2;
            }
            else if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                return text[start..i];
            }
        }

        return text[start..];
    }

    /// <summary>Index of the closing quote of the string literal that opens at <paramref name="open"/> (regular, verbatim or interpolated).</summary>
    private static int EndOfString(string text, int open)
    {
        var verbatim = open > 0 && (text[open - 1] == '@' || (open > 1 && text[open - 1] == '$' && text[open - 2] == '@'));
        for (var i = open + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i;
            }
        }

        return text.Length;
    }

    [GeneratedRegex(@"(\w+)\.Map(?:Get|Post|Put|Delete|Patch|Methods|HealthChecks|Mcp)\(\s*([^,)]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MapCall();

    [GeneratedRegex(@"var\s+(\w+)\s*=\s*\w+\.MapGroup\([^;]*\.RequireAuthorization\(\s*[^)\s]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProtectedGroup();

    /// <summary>RequireAuthorization with an argument (a policy); without one it only demands a signed-in user.</summary>
    [GeneratedRegex(@"\.RequireAuthorization\(\s*[^)\s]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamedPolicy();

    [GeneratedRegex(@"const\s+string\s+(\w+)\s*=\s*(?:(\w+)\s*\+\s*)?""([^""]*)""\s*;", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Constant();
}
