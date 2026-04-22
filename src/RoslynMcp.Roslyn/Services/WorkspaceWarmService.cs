using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Opt-in <c>workspace_warm</c> implementation. Walks every project in the workspace (or
/// the caller-provided filter subset), forces <see cref="Project.GetCompilationAsync"/>,
/// and then resolves one semantic model per project to stabilize Roslyn's semantic cache.
/// The first post-<c>workspace_load</c> read-side call on a mid-sized solution pays a
/// ~4600ms cold cost vs ~40ms warm; warming up front shifts that cost into this call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cold-vs-warm detection.</b> We call <see cref="Project.TryGetCompilation"/> BEFORE
/// <see cref="Project.GetCompilationAsync"/>. <see cref="Project.TryGetCompilation"/>
/// returns <see langword="true"/> only when the compilation is already present in the
/// project's internal cache; a <see langword="false"/> result means the project was cold
/// going into the warm. <see cref="WorkspaceWarmResult.ColdCompilationCount"/> increments
/// once per cold project so a repeat warm on an unchanged workspace reports 0.
/// </para>
/// <para>
/// <b>Per-project fault isolation.</b> A warm on one project may raise (e.g. a malformed
/// project file the MSBuild workspace couldn't fully rehydrate). We catch
/// <see cref="OperationCanceledException"/> to honor the caller's cancellation and let it
/// propagate; any other exception is logged at warning, the project is marked in
/// <see cref="WorkspaceWarmResult.ProjectsWarmed"/> with a "warm-failed" suffix, and the
/// sweep continues so a single bad project can't stop the rest of the warm.
/// </para>
/// <para>
/// <b>Gate discipline.</b> The calling <see cref="Host.Stdio.Tools.WorkspaceWarmTools"/>
/// invokes this service under the per-workspace read gate (see
/// <see cref="IWorkspaceExecutionGate.RunReadAsync{T}"/>). Multiple warms on the same
/// workspace run concurrently; a warm is blocked by any in-flight write (e.g. an
/// <c>*_apply</c> refactor). Warming does not mutate source files, only Roslyn's internal
/// compilation and semantic caches.
/// </para>
/// </remarks>
public sealed class WorkspaceWarmService : IWorkspaceWarmService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<WorkspaceWarmService> _logger;

    public WorkspaceWarmService(IWorkspaceManager workspace, ILogger<WorkspaceWarmService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<WorkspaceWarmResult> WarmAsync(string workspaceId, string[]? projects, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var projectFilter = BuildProjectFilter(projects);
        var warmedNames = new List<string>();
        var coldCount = 0;

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;

            if (projectFilter is not null && !projectFilter.Contains(project.Name))
                continue;

            var outcome = await WarmProjectAsync(project, ct).ConfigureAwait(false);
            warmedNames.Add(outcome.DisplayName);
            if (outcome.WasCold) coldCount++;
        }

        stopwatch.Stop();
        return new WorkspaceWarmResult(
            WorkspaceId: workspaceId,
            ProjectsWarmed: warmedNames,
            ElapsedMs: stopwatch.ElapsedMilliseconds,
            ColdCompilationCount: coldCount);
    }

    private static HashSet<string>? BuildProjectFilter(string[]? projects)
    {
        if (projects is null || projects.Length == 0) return null;
        return new HashSet<string>(projects, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ProjectWarmOutcome> WarmProjectAsync(Project project, CancellationToken ct)
    {
        // Cold-vs-warm detection: TryGetCompilation pre-check. If the compilation is
        // already in Roslyn's cache this returns true without allocating work.
        var wasCold = !project.TryGetCompilation(out _);

        try
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
            {
                // Non-C#/VB project (e.g. language not supported by the workspace). Skip
                // semantic model warm but keep the entry in the roster so the caller sees
                // which projects were visited.
                return new ProjectWarmOutcome(project.Name, wasCold);
            }

            // Stabilize the semantic-model cache: pick the first compiled document in the
            // project and resolve its syntax tree → semantic model. That populates the
            // shared per-compilation semantic cache, so the first post-warm
            // symbol_search / find_references call against this project hits the fast path.
            var firstDocument = project.Documents.FirstOrDefault();
            if (firstDocument is not null)
            {
                var tree = await firstDocument.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (tree is not null)
                {
                    _ = compilation.GetSemanticModel(tree);
                }
            }

            return new ProjectWarmOutcome(project.Name, wasCold);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "workspace_warm: project '{ProjectName}' failed to warm.", project.Name);
            return new ProjectWarmOutcome($"{project.Name} (warm-failed: {ex.GetType().Name})", wasCold);
        }
    }

    private readonly record struct ProjectWarmOutcome(string DisplayName, bool WasCold);
}
