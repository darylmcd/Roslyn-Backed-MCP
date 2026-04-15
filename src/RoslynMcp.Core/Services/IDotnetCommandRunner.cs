using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 4: a regex pattern that terminates a running dotnet process early when it appears in
/// stdout/stderr. Used by <see cref="IDotnetCommandRunner.RunAsync(string, string, IReadOnlyList{string}, IReadOnlyList{EarlyKillPattern}?, CancellationToken)"/>
/// so callers can short-circuit MSBuild retry loops (e.g. MSB3027 file locks) without waiting
/// the full ~10s retry budget.
/// </summary>
public sealed record EarlyKillPattern(Regex Pattern, string Reason);

/// <summary>
/// Executes <c>dotnet</c> CLI commands as child processes and captures their output.
/// </summary>
public interface IDotnetCommandRunner
{
    /// <summary>
    /// Runs a <c>dotnet</c> command with the given arguments and returns the captured execution result.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the process.</param>
    /// <param name="targetPath">The solution or project path passed as the primary target argument.</param>
    /// <param name="arguments">Additional arguments to pass to <c>dotnet</c>.</param>
    /// <param name="ct">Cancellation token. Cancellation will attempt to kill the child process.</param>
    Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct);

    /// <summary>
    /// Item 4: streaming variant with an optional set of <see cref="EarlyKillPattern"/> entries.
    /// When any pattern matches a line of stdout or stderr while the process is still running,
    /// the runner kills the process tree and stamps
    /// <see cref="CommandExecutionDto.EarlyKillReason"/> with the pattern's reason string.
    /// Implementations should fall back to the base <see cref="RunAsync(string, string, IReadOnlyList{string}, CancellationToken)"/>
    /// semantics when <paramref name="earlyKillPatterns"/> is <see langword="null"/> or empty.
    /// </summary>
    Task<CommandExecutionDto> RunAsync(
        string workingDirectory,
        string targetPath,
        IReadOnlyList<string> arguments,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        CancellationToken ct) =>
        RunAsync(workingDirectory, targetPath, arguments, ct);
}
