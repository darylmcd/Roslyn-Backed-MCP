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
    /// Streaming variant that kills the process when an early-kill pattern matches.
    /// Implementations must preserve the supplied patterns.
    /// </summary>
    Task<CommandExecutionDto> ExecuteAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        CancellationToken ct);

    /// <summary>Parses raw test output inside execution, then redacts every outward result string.</summary>
    Task<TestRunResultDto> ExecuteTestAsync(
        string workspaceId, string targetPath, IReadOnlyList<string> arguments,
        Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults,
        TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
        Func<CancellationToken, Task> authorizeStart, CancellationToken ct);

    /// <summary>
    /// Runs an authority-bound producer. Revalidate its captured inputs and current grants
    /// after acquiring both command gates, immediately before invoking the command runner.
    /// Authorization shares the command timeout and must complete before any process effects.
    /// </summary>
    Task<CommandExecutionDto> ExecuteAuthorizedAsync(
        string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
        Func<CancellationToken, Task> authorizeStart, CancellationToken ct);

    /// <summary>Parses raw build diagnostics before redaction, retaining private identities for span enrichment.</summary>
    Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Resolves a project by name or path within a workspace.
    /// </summary>
    ProjectStatusDto ResolveProject(string workspaceId, string projectName);
}
