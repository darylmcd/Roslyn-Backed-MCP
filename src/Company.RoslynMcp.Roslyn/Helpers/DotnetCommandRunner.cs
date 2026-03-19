using System.Buffers;
using System.Diagnostics;
using System.Text;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;

namespace Company.RoslynMcp.Roslyn.Services;

/// <summary>
/// Executes <c>dotnet</c> CLI commands as child processes and captures their standard output
/// and error streams, bounded to prevent excessive memory consumption.
/// </summary>
/// <remarks>
/// Output streams are bounded to <c>12000</c> characters: if the output exceeds this limit,
/// only the final 12000 characters are retained.
/// Cancellation kills the entire process tree.
/// </remarks>
public sealed class DotnetCommandRunner : IDotnetCommandRunner
{
    private const int OutputLimit = 12000;
    private const int ReadBufferSize = 4096;

    public async Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        using var cancellationRegistration = ct.Register(static state =>
        {
            try
            {
                ((Process)state!).Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }, process);

        var stdOutTask = ReadBoundedAsync(process.StandardOutput, ct);
        var stdErrTask = ReadBoundedAsync(process.StandardError, ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        stopwatch.Stop();

        return new CommandExecutionDto(
            Command: "dotnet",
            Arguments: arguments,
            WorkingDirectory: workingDirectory,
            TargetPath: targetPath,
            ExitCode: process.ExitCode,
            Succeeded: process.ExitCode == 0,
            DurationMs: stopwatch.ElapsedMilliseconds,
            StdOut: stdOut,
            StdErr: stdErr);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = ArrayPool<char>.Shared.Rent(ReadBufferSize);
        var bounded = new BoundedTextBuffer(OutputLimit);

        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(0, ReadBufferSize), ct).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                bounded.Append(buffer.AsSpan(0, count));
            }

            return bounded.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private sealed class BoundedTextBuffer(int maxLength)
    {
        private readonly StringBuilder _builder = new(Math.Min(maxLength, ReadBufferSize));

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.Length >= maxLength)
            {
                _builder.Clear();
                _builder.Append(value[^maxLength..]);
                return;
            }

            _builder.Append(value);
            if (_builder.Length > maxLength)
            {
                _builder.Remove(0, _builder.Length - maxLength);
            }
        }

        public override string ToString() => _builder.ToString();
    }
}
