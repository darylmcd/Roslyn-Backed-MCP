using System.Buffers;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Grace window for draining stdout/stderr after the child process exits. MSBuild worker
    /// nodes and VBCSCompiler spawned by build-ish commands inherit the redirected pipe write
    /// handles and can outlive the child by many minutes (node idle timeout is 15 minutes, and
    /// continuous reuse resets it) — pipe EOF never arrives while they hold the write end, so an
    /// unbounded read deadlocks long after the command finished. The child's own buffered output
    /// flushes within milliseconds of its exit, so a short bounded drain preserves complete
    /// output without inheriting the descendants' lifetime.
    /// </summary>
    internal static readonly TimeSpan PostExitDrainGracePeriod = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, int, string, string, Exception?> LogProcessTreeKillFailed =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogProcessTreeKillFailed)),
            "Failed to kill dotnet process tree for process {ProcessId} after {KillReason} while running {TargetPath}.");

    private readonly int _outputLimit;
    private readonly ILogger<DotnetCommandRunner>? _logger;
    private readonly Action<Process> _killProcessTree;

    public DotnetCommandRunner(int outputLimit = DefaultOutputLimit)
        : this(logger: null, outputLimit, KillProcessTree)
    {
    }

    public DotnetCommandRunner(ILogger<DotnetCommandRunner> logger)
        : this(logger, DefaultOutputLimit, KillProcessTree)
    {
    }

    internal DotnetCommandRunner(
        int outputLimit,
        Action<Process> killProcessTree,
        ILogger<DotnetCommandRunner>? logger = null)
        : this(logger, outputLimit, killProcessTree)
    {
    }

    private DotnetCommandRunner(
        ILogger<DotnetCommandRunner>? logger,
        int outputLimit,
        Action<Process> killProcessTree)
    {
        _outputLimit = outputLimit > 0 ? outputLimit : DefaultOutputLimit;
        _logger = logger;
        _killProcessTree = killProcessTree;
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
        var startInfo = CreateStartInfo(workingDirectory, arguments);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        using var cancellationRegistration = ct.Register(static state =>
        {
            var kill = (ProcessKillState)state!;
            kill.Runner.TryKillProcessTree(kill.Process, kill.TargetPath, "cancellation");
        }, new ProcessKillState(this, process, targetPath));

        // Item 4: share a single early-kill coordinator across both stream readers so either
        // stdout or stderr can terminate the process on first match.
        var earlyKill = earlyKillPatterns is { Count: > 0 }
            ? new EarlyKillCoordinator(this, process, targetPath, earlyKillPatterns)
            : null;

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stdOutTask = ReadBoundedAsync(process.StandardOutput, _outputLimit, earlyKill, drainCts.Token, ct);
        var stdErrTask = ReadBoundedAsync(process.StandardError, _outputLimit, earlyKill, drainCts.Token, ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        // The child has exited; only handle-inheriting descendants can still hold the pipe open.
        drainCts.CancelAfter(PostExitDrainGracePeriod);
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

    internal static ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> arguments)
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

        // Reusable MSBuild worker nodes inherit this process's redirected pipe handles and idle
        // for 15 minutes after the build, holding the pipe open (see PostExitDrainGracePeriod)
        // and accumulating orphaned dotnet processes on the host. Spawned commands are one-shot
        // from the runner's perspective, so node reuse buys nothing here.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void KillProcessTree(Process process) => process.Kill(entireProcessTree: true);

    private void TryKillProcessTree(Process process, string targetPath, string killReason)
    {
        try
        {
            _killProcessTree(process);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogProcessTreeKillFailed(_logger, process.Id, killReason, targetPath, ex);
            }
        }
    }

    internal static async Task<string> ReadBoundedAsync(
        StreamReader reader, int outputLimit,
        CancellationToken drainCt, CancellationToken callerCt)
        => await ReadBoundedAsync(reader, outputLimit, earlyKill: null, drainCt, callerCt).ConfigureAwait(false);

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int outputLimit, EarlyKillCoordinator? earlyKill,
        CancellationToken drainCt, CancellationToken callerCt)
    {
        var buffer = ArrayPool<char>.Shared.Rent(ReadBufferSize);
        var bounded = new BoundedTextBuffer(outputLimit);

        try
        {
            while (true)
            {
                int count;
                try
                {
                    count = await reader.ReadAsync(buffer.AsMemory(0, ReadBufferSize), drainCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
                {
                    // Post-exit drain grace expired: a handle-inheriting descendant (MSBuild
                    // worker node, VBCSCompiler) still holds the pipe write end open. The child
                    // itself already exited, so everything it wrote has been read — stop waiting
                    // for an EOF that only arrives when those descendants die.
                    break;
                }

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
        private readonly DotnetCommandRunner _runner;
        private readonly Process _process;
        private readonly string _targetPath;
        private readonly IReadOnlyList<EarlyKillPattern> _patterns;
        private int _killed;

        public EarlyKillCoordinator(
            DotnetCommandRunner runner,
            Process process,
            string targetPath,
            IReadOnlyList<EarlyKillPattern> patterns)
        {
            _runner = runner;
            _process = process;
            _targetPath = targetPath;
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
                    _runner.TryKillProcessTree(_process, _targetPath, "early-kill pattern");
                    return;
                }
            }
        }
    }

    private sealed record ProcessKillState(
        DotnetCommandRunner Runner,
        Process Process,
        string TargetPath);

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
