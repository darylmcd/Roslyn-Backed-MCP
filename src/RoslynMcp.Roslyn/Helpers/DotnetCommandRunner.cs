using System.Buffers;
using System.Diagnostics;
using System.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Executes <c>dotnet</c> CLI commands as child processes and captures their standard output
/// and error streams, bounded to prevent excessive memory consumption.
/// </summary>
/// <remarks>
/// Output streams are bounded to <c>12000</c> characters: if the output exceeds this limit,
/// only the final 12000 characters are retained.
/// Cancellation kills the entire process tree.
///
/// <para>
/// Item 4 (<c>test-run-file-lock-fast-fail</c>): when callers supply
/// <see cref="EarlyKillPattern"/> entries, the runner scans each freshly-read buffer slice for a
/// match and, on hit, terminates the process tree and populates
/// <see cref="CommandExecutionDto.EarlyKillReason"/> so callers (e.g.
/// <c>TestRunnerService</c>) can short-circuit MSBuild's 10×1s MSB3027 retry loop.
/// </para>
/// </remarks>
public sealed class DotnetCommandRunner : IDotnetCommandRunner
{
    private const int DefaultOutputLimit = 12_000;
    private const int ReadBufferSize = 4096;

    private readonly int _outputLimit;

    public DotnetCommandRunner(int outputLimit = DefaultOutputLimit)
    {
        _outputLimit = outputLimit > 0 ? outputLimit : DefaultOutputLimit;
    }

    public Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
        => RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns: null, ct);

    public async Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
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

        // Item 4: share a single early-kill coordinator across both stream readers so either
        // stdout or stderr can terminate the process on first match.
        var earlyKill = earlyKillPatterns is { Count: > 0 }
            ? new EarlyKillCoordinator(process, earlyKillPatterns)
            : null;

        var stdOutTask = ReadBoundedAsync(process.StandardOutput, _outputLimit, earlyKill, ct);
        var stdErrTask = ReadBoundedAsync(process.StandardError, _outputLimit, earlyKill, ct);

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
            Succeeded: process.ExitCode == 0 && earlyKill?.KillReason is null,
            DurationMs: stopwatch.ElapsedMilliseconds,
            StdOut: stdOut,
            StdErr: stdErr,
            EarlyKillReason: earlyKill?.KillReason);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int outputLimit, EarlyKillCoordinator? earlyKill, CancellationToken ct)
    {
        var buffer = ArrayPool<char>.Shared.Rent(ReadBufferSize);
        var bounded = new BoundedTextBuffer(outputLimit);

        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(0, ReadBufferSize), ct).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                var slice = buffer.AsSpan(0, count);
                bounded.Append(slice);

                // Item 4: scan only the fresh slice (not the whole bounded buffer) to keep the
                // match cost per buffer-read O(patterns × slice-length), regardless of how much
                // prior output accumulated.
                earlyKill?.ScanAndMaybeKill(slice);
            }

            return bounded.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Coordinates early-kill state shared by stdout and stderr readers. First match wins; the
    /// reason string is surfaced on the resulting <see cref="CommandExecutionDto"/>.
    /// </summary>
    private sealed class EarlyKillCoordinator
    {
        private readonly Process _process;
        private readonly IReadOnlyList<EarlyKillPattern> _patterns;
        private int _killed;

        public EarlyKillCoordinator(Process process, IReadOnlyList<EarlyKillPattern> patterns)
        {
            _process = process;
            _patterns = patterns;
        }

        public string? KillReason { get; private set; }

        public void ScanAndMaybeKill(ReadOnlySpan<char> slice)
        {
            if (_killed != 0 || slice.Length == 0) return;

            // Regex doesn't accept spans directly; allocate one string per slice (4KB max).
            // At typical MSBuild retry rate this is <= 10 allocations per failed test run.
            var text = slice.ToString();
            foreach (var pattern in _patterns)
            {
                if (pattern.Pattern.IsMatch(text))
                {
                    if (Interlocked.Exchange(ref _killed, 1) != 0) return;
                    KillReason = pattern.Reason;
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Process may have already exited between the match and kill; ignore.
                    }
                    return;
                }
            }
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
