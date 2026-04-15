using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Executes <c>dotnet</c> CLI commands with concurrency gating (global + per-workspace)
/// and timeout management. Shared infrastructure for <see cref="IBuildService"/>
/// and <see cref="ITestRunnerService"/>.
/// </summary>
public interface IGatedCommandExecutor : IDisposable
{
    /// <summary>
    /// Runs a gated <c>dotnet</c> command with concurrency control and timeout enforcement.
    /// </summary>
    Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct);

    /// <summary>
    /// Item 4: streaming variant that kills the spawned process early when any of
    /// <paramref name="earlyKillPatterns"/> matches stdout or stderr. Default implementation
    /// ignores the patterns and delegates to <see cref="ExecuteAsync(string, string, IReadOnlyList{string}, TimeSpan, CancellationToken)"/>
    /// so unit tests and legacy executors keep compiling.
    /// </summary>
    Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        CancellationToken ct) =>
        ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);

    /// <summary>
    /// Resolves a project by name or path within a workspace.
    /// </summary>
    ProjectStatusDto ResolveProject(string workspaceId, string projectName);
}
