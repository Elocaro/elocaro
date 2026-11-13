using System.Reflection;
using System.Text.RegularExpressions;
using Elocaro.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Elocaro.Architecture.Tests;

/// <summary>
/// The MCP interface is a second way to the services, and the administration services check no permissions themselves
/// (only the page or endpoint in front of them does). These rules keep every reading tool read-only and behind a policy, and every writing tool
/// behind the write policy, restricted to types that can be taken back (ADR 0007); see docs/mcp.md.
/// </summary>
public partial class McpRulesTests
{
    private static readonly Assembly Web = typeof(Program).Assembly;

    /// <summary>Services a writing tool may use. Each must only change entity types the <c>UndoPolicy</c> classifies as Restorable.</summary>
    private static readonly HashSet<string> WriteServices = ["IPropertyService", "IPersonService", "ILeaseService", "IMeterService", "TimeProvider"];

    private static List<Type> ToolTypes() =>
        Web.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();

    private static bool IsWriting(Type type) => type.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == Policies.MasterDataWrite);

    private static List<MethodInfo> ToolMethods(Func<Type, bool> filter) =>
        ToolTypes().Where(filter).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null).ToList();

    [Fact]
    public void There_are_tools()
    {
        Assert.NotEmpty(ToolTypes());
        Assert.NotEmpty(ToolMethods(_ => true));
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
        var open = ToolTypes().Cast<MemberInfo>().Concat(ToolMethods(_ => true))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(open);
    }

    [Fact]
    public void Every_reading_tool_is_marked_read_only()
    {
        var notMarked = ToolMethods(t => !IsWriting(t)).Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not { ReadOnly: true, Destructive: false }).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();
        Assert.True(notMarked.Count == 0, "Reading tools without ReadOnly = true and Destructive = false: " + string.Join(", ", notMarked));
    }

    [Fact]
    public void Reading_tools_call_no_changing_service_method()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Repository.Web, "Mcp", "Tools"), "*.cs")
            .Where(f => ToolTypes().Where(t => !IsWriting(t)).Any(t => Path.GetFileNameWithoutExtension(f) == t.Name))
            .SelectMany(f => ChangingCall().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.True(offenders.Count == 0, "Changing calls in reading MCP tools:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Writing_tools_are_not_read_only_and_never_destructive()
    {
        var wrong = ToolMethods(IsWriting).Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not { ReadOnly: false, Destructive: false }).Select(m => m.Name).ToList();
        Assert.True(wrong.Count == 0, "Writing tools must say ReadOnly = false, Destructive = false (nothing is deleted through MCP): " + string.Join(", ", wrong));
    }

    [Fact]
    public void Writing_tools_use_only_services_of_restorable_types()
    {
        var offenders = ToolTypes().Where(IsWriting)
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .Select(p => p.ParameterType.Name)
            .Where(n => !WriteServices.Contains(n))
            .Distinct()
            .ToList();
        Assert.True(offenders.Count == 0, "Writing tools use services outside the allowed list (see UndoPolicy, ADR 0007): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_tool_call_runs_inside_an_operation()
    {
        var source = File.ReadAllText(Path.Combine(Repository.Web, "Mcp", "McpExtensions.cs"));
        Assert.Contains("IOperationScope", source, StringComparison.Ordinal);
        Assert.Contains("ChangeChannel.Mcp", source, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\.(Create|Update|Delete|Remove|Record|Release|Issue|Import|Set|Add|Save|Send|Generate|Refresh|Ignore|Reopen|Discard|Dismiss|Terminate|Invite|Lock|Unlock|Revoke|Complete|Renew)\w*Async\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChangingCall();
}
