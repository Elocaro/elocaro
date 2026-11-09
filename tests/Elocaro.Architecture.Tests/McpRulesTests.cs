using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// The MCP interface is a second way to the services, and the administration services check no permissions themselves
/// (only the page or endpoint in front of them does). These rules keep every tool read-only and behind a policy; see docs/mcp.md.
/// </summary>
public partial class McpRulesTests
{
    private static readonly Assembly Web = typeof(Program).Assembly;

    private static List<Type> ToolTypes() =>
        Web.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();

    private static List<MethodInfo> ToolMethods() =>
        ToolTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null).ToList();

    [Fact]
    public void There_are_tools()
    {
        Assert.NotEmpty(ToolTypes());
        Assert.NotEmpty(ToolMethods());
    }

    [Fact]
    public void Every_tool_class_demands_a_policy()
    {
        var without = ToolTypes().Where(t => t.GetCustomAttributes<AuthorizeAttribute>().All(a => string.IsNullOrWhiteSpace(a.Policy))).Select(t => t.Name).ToList();
        Assert.True(without.Count == 0, "Tool classes without [Authorize(Policy = ...)]: " + string.Join(", ", without));
    }

    [Fact]
    public void Tools_are_not_opened_to_everyone()
    {
        var open = ToolTypes().Cast<MemberInfo>().Concat(ToolMethods())
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(open);
    }

    [Fact]
    public void Every_tool_is_marked_read_only()
    {
        var notMarked = ToolMethods().Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not { ReadOnly: true, Destructive: false }).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();
        Assert.True(notMarked.Count == 0, "Tools without ReadOnly = true and Destructive = false: " + string.Join(", ", notMarked));
    }

    [Fact]
    public void Tools_call_no_changing_service_method()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Repository.Web, "Mcp", "Tools"), "*.cs")
            .SelectMany(f => ChangingCall().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0, "Changing calls in MCP tools:\n" + string.Join("\n", offenders));
    }

    [GeneratedRegex(@"\.(Create|Update|Delete|Remove|Record|Release|Issue|Import|Set|Add|Save|Send|Generate|Refresh|Ignore|Reopen|Discard|Dismiss|Terminate|Invite|Lock|Unlock|Revoke|Complete|Renew)\w*Async\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChangingCall();
}
