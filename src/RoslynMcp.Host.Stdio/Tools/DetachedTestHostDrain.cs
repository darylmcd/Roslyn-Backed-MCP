using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Host.Stdio.Runtime;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// A <c>workspace_close(drainProcesses: true)</c> candidate that the drain left running. Either
/// its executable path could not be read, so the drain could not tell whether it belongs to the
/// workspace, or it belongs to the workspace and was not terminated. It may still hold file locks
/// under the workspace.
/// </summary>
/// <param name="ProcessName">The enumerated process name: <c>testhost</c> or <c>vstest.console</c>.</param>
/// <param name="ProcessId">The operating-system process id.</param>
/// <param name="Reason">A stable kebab-case code: <see cref="ProcessExecutablePathResolver.AccessDeniedReason"/>,
/// <see cref="ProcessExecutablePathResolver.PathQueryFailedReason"/>,
/// <see cref="DetachedTestHostDrain.TerminateFailedReason"/>, or
/// <see cref="DetachedTestHostDrain.DrainCancelledReason"/>.</param>
internal sealed record UndrainedProcess(string ProcessName, int ProcessId, string Reason);

/// <summary>Outcome of one detached test-host drain pass.</summary>
/// <param name="Undrained">Candidates left running, in enumeration order. Empty when none.</param>
/// <param name="FirstFailure">The first unexpected exception, for the caller to report once.
/// <see langword="null"/> when none occurred.</param>
internal sealed record DetachedTestHostDrainResult(
    IReadOnlyList<UndrainedProcess> Undrained,
    Exception? FirstFailure);

/// <summary>
/// Terminates detached <c>testhost</c> / <c>vstest.console</c> processes whose executable resides
/// under a workspace directory. <c>dotnet test</c> spawns them, they survive
/// <c>dotnet build-server shutdown</c>, and they hold <c>tests/.../bin</c> file locks that block
/// <c>git worktree remove</c> on Windows.
/// </summary>
/// <remarks>
/// <para>The workspace close has already committed when this runs, so nothing here throws.
/// Unexpected exceptions come back as <see cref="DetachedTestHostDrainResult.FirstFailure"/>.
/// A candidate is never dropped silently. One that could not be inspected or terminated comes
/// back in <see cref="DetachedTestHostDrainResult.Undrained"/> and is logged at Warning. A
/// process whose executable path cannot be read is never terminated, because its location
/// is unknown. Only a process that has already exited counts as benign.</para>
/// <para>After the cancellation token fires (the cleanup budget ran out or the
/// caller cancelled), the drain stops terminating processes. It still reports each in-workspace
/// candidate it did not terminate.</para>
/// </remarks>
internal static class DetachedTestHostDrain
{
    /// <summary>The candidate belongs to the workspace, but terminating it failed.</summary>
    public const string TerminateFailedReason = "terminate-failed";

    /// <summary>The cleanup budget or the caller's cancellation ended the drain before this
    /// in-workspace candidate was terminated.</summary>
    public const string DrainCancelledReason = "drain-cancelled";

    // Match both the .NET test host ("testhost") and the legacy console runner
    // ("vstest.console"). Process names are extensionless: .exe on Windows, bare on Unix.
    private static readonly string[] s_processNames = ["testhost", "vstest.console"];

    public static DetachedTestHostDrainResult Run(
        string workingDirectory,
        Func<string, Process[]> getProcessesByName,
        Func<Process, ProcessExecutablePathResolution> resolveExecutablePath,
        ILogger? logger,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        // Boundary guard: only TRUE DESCENDANTS of workingDirectory match. A bare
        // StartsWith(workingDirectory) false-positives on a sibling worktree whose path is a
        // string prefix (".../wt-foo" vs ".../wt-foo-bar"). That would kill an unrelated
        // worktree's testhost tree mid-run, and .worktrees/ holds concurrent siblings during
        // parallel sweeps. Normalize to a full path and append exactly one separator so the
        // prefix can only match a child path.
        string workingDirectoryPrefix;
        try
        {
            workingDirectoryPrefix =
                Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
        catch (Exception ex)
        {
            // A malformed working directory cannot be normalized. Skip the drain entirely
            // rather than fall back to an unguarded match.
            return new DetachedTestHostDrainResult([], ex);
        }

        var undrained = new List<UndrainedProcess>();
        Exception? firstFailure = null;
        foreach (var processName in s_processNames)
        {
            Process[] candidates;
            try
            {
                candidates = getProcessesByName(processName);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                continue;
            }

            foreach (var process in candidates)
            {
                try
                {
                    var reason = DrainCandidate(
                        process,
                        processName,
                        workingDirectoryPrefix,
                        resolveExecutablePath,
                        logger,
                        workspaceId,
                        cancellationToken,
                        out var failure);
                    firstFailure ??= failure;
                    if (reason is not null)
                    {
                        var processId = process.Id;
                        undrained.Add(new UndrainedProcess(processName, processId, reason));
                        logger?.LogWarning(
                            "workspace_close drain left {ProcessName} (pid {ProcessId}) running for workspace " +
                            "{WorkspaceId}: {Reason}. It may still hold file locks under the workspace.",
                            processName,
                            processId,
                            workspaceId,
                            reason);
                    }
                }
                catch (Exception ex)
                {
                    // Only reachable if the Process object cannot report its id.
                    firstFailure ??= ex;
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch (Exception ex)
                    {
                        firstFailure ??= ex;
                    }
                }
            }
        }

        return new DetachedTestHostDrainResult(undrained, firstFailure);
    }

    /// <returns><see langword="null"/> when the candidate needs no report: it was terminated,
    /// lives outside the workspace, or had already exited. Otherwise the undrained reason.</returns>
    private static string? DrainCandidate(
        Process process,
        string processName,
        string workingDirectoryPrefix,
        Func<Process, ProcessExecutablePathResolution> resolveExecutablePath,
        ILogger? logger,
        string workspaceId,
        CancellationToken cancellationToken,
        out Exception? failure)
    {
        failure = null;
        string executablePath;
        try
        {
            var resolution = resolveExecutablePath(process);
            if (resolution.Status == ProcessExecutablePathStatus.Exited)
            {
                return null;
            }

            if (resolution.Status != ProcessExecutablePathStatus.Resolved || string.IsNullOrEmpty(resolution.Path))
            {
                return resolution.Reason ?? ProcessExecutablePathResolver.PathQueryFailedReason;
            }

            executablePath = Path.GetFullPath(resolution.Path);
        }
        catch (Exception ex)
        {
            failure = ex;
            return ProcessExecutablePathResolver.PathQueryFailedReason;
        }

        if (!executablePath.StartsWith(workingDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            failure = new OperationCanceledException(cancellationToken);
            return DrainCancelledReason;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            failure = ex;
            return TerminateFailedReason;
        }

        logger?.LogDebug(
            "workspace_close terminated detached {ProcessName} (pid {ProcessId}) for workspace {WorkspaceId}.",
            processName,
            process.Id,
            workspaceId);
        return null;
    }
}
