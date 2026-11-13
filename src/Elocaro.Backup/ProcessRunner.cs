using System.Diagnostics;
using System.Text;

namespace Elocaro.Backup;

public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>Starts external programs (restic, pg_dump, …). Arguments are passed as a list, never through a shell.</summary>
public interface IProcessRunner
{
    /// <param name="onOutput">Called with every line of standard output while the program runs (progress).</param>
    Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, Action<string>? onOutput = null);
}

internal sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Only the end of the output is kept (restic prints its summary last).</summary>
    private const int MaxOutput = 256 * 1024;

    public async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken, Action<string>? onOutput = null)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line);
                if (output.Length > MaxOutput)
                {
                    output.Remove(0, output.Length - MaxOutput);
                }
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            Append(e.Data);
            if (e.Data is not null && onOutput is not null)
            {
                try
                {
                    onOutput(e.Data);
                }
#pragma warning disable CA1031 // Progress is a courtesy: whatever goes wrong while reporting it must not stop the backup.
                catch (Exception)
                {
                    // ignored
                }
#pragma warning restore CA1031
            }
        };
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        // The parameterless overload waits until the asynchronous output events have been delivered.
        process.WaitForExit();
        lock (output)
        {
            return new ProcessResult(process.ExitCode, output.ToString());
        }
    }
}
