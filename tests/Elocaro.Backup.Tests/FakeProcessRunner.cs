using Elocaro.Backup;

namespace Elocaro.Backup.Tests;

/// <summary>Records calls and answers them from a script; unknown calls succeed with empty output.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(string File, string[] Arguments, Dictionary<string, string>? Environment)> calls = [];

    /// <summary>Answer for a call, selected by program and first argument (e.g. <c>restic cat</c>).</summary>
    public Dictionary<string, Func<string[], ProcessResult>> Script { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<(string File, string[] Arguments, Dictionary<string, string>? Environment)> Calls => calls;

    public IEnumerable<string> Commands => calls.Select(c => $"{c.File} {(c.Arguments.Length > 0 ? c.Arguments[0] : string.Empty)}".TrimEnd());

    public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        var args = arguments.ToArray();
        calls.Add((file, args, environment?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
        var key = $"{file} {(args.Length > 0 ? args[0] : string.Empty)}".TrimEnd();
        return Task.FromResult(Script.TryGetValue(key, out var answer) ? answer(args) : new ProcessResult(0, string.Empty));
    }
}
