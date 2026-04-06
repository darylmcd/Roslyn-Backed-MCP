using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Per-workspace, version-keyed cache for <see cref="Compilation"/> and
/// <see cref="CompilationWithAnalyzers"/>. See <see cref="ICompilationCache"/> for the
/// rationale and concurrency contract.
/// </summary>
/// <remarks>
/// The cache stores <see cref="Task{TResult}"/> values rather than materialized compilations,
/// so concurrent first-callers awaiting the same key share a single compilation pass instead
/// of racing to start their own. Stale entries are replaced atomically when a higher
/// workspace version is observed; we don't bother evicting older entries proactively because
/// each project has at most one slot per cache and stale tasks are released as soon as the
/// new entry is installed.
/// </remarks>
public sealed class CompilationCache : ICompilationCache, IDisposable
{
    private readonly IWorkspaceManager _workspaceManager;

    private sealed record CompilationEntry(int Version, Task<Compilation?> Compilation);
    private sealed record AnalyzerEntry(int Version, Task<CompilationWithAnalyzers?> Bound);

    // Two parallel maps. Splitting them lets a service that only needs the raw Compilation
    // (e.g., dead-code analysis) skip warming the analyzer pipeline.
    private readonly ConcurrentDictionary<(string WorkspaceId, ProjectId ProjectId), CompilationEntry> _compilations = new();
    private readonly ConcurrentDictionary<(string WorkspaceId, ProjectId ProjectId), AnalyzerEntry> _analyzerBound = new();

    public CompilationCache(IWorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
        // Free per-workspace cache slots when the workspace closes. Without this hook, closed
        // workspace IDs (GUIDs) accumulate forever — stale entries are functionally inert
        // because every read re-checks GetCurrentVersion, but they hold Compilation references
        // until process exit.
        _workspaceManager.WorkspaceClosed += Invalidate;
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= Invalidate;
    }

    public Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var version = _workspaceManager.GetCurrentVersion(workspaceId);
        var key = (workspaceId, project.Id);

        if (_compilations.TryGetValue(key, out var existing) && existing.Version == version)
        {
            return existing.Compilation;
        }

        // Cache miss or stale: install a fresh task. Multiple concurrent first-callers may
        // race here; AddOrUpdate's atomicity ensures only one entry is stored, and any racer
        // that lost will see the winner's task on the next read. The lost-racer's task will
        // run to completion but its result is discarded — acceptable cost for the simplicity
        // of not holding a per-key lock.
        var compilationTask = project.GetCompilationAsync(ct);
        var entry = new CompilationEntry(version, compilationTask);
        _compilations.AddOrUpdate(key, entry, (_, current) => current.Version >= version ? current : entry);

        // Return whichever entry actually won the AddOrUpdate so all callers observe the
        // same task instance.
        return _compilations.TryGetValue(key, out var winner) && winner.Version == version
            ? winner.Compilation
            : compilationTask;
    }

    public async Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var version = _workspaceManager.GetCurrentVersion(workspaceId);
        var key = (workspaceId, project.Id);

        if (_analyzerBound.TryGetValue(key, out var existing) && existing.Version == version)
        {
            return await existing.Bound.ConfigureAwait(false);
        }

        var task = BuildCompilationWithAnalyzersAsync(workspaceId, project, ct);
        var entry = new AnalyzerEntry(version, task);
        _analyzerBound.AddOrUpdate(key, entry, (_, current) => current.Version >= version ? current : entry);

        var winner = _analyzerBound.TryGetValue(key, out var stored) && stored.Version == version
            ? stored.Bound
            : task;
        return await winner.ConfigureAwait(false);
    }

    private async Task<CompilationWithAnalyzers?> BuildCompilationWithAnalyzersAsync(
        string workspaceId, Project project, CancellationToken ct)
    {
        var compilation = await GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
        if (compilation is null) return null;

        var analyzers = project.AnalyzerReferences
            .SelectMany(reference => reference.GetAnalyzers(project.Language))
            .ToImmutableArray();
        if (analyzers.Length == 0) return null;

        return compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                options: project.AnalyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));
    }

    public void Invalidate(string workspaceId)
    {
        // Stale entries (workspace closed, version bumped) are functionally inert because
        // every read re-checks GetCurrentVersion, but they hold Compilation references until
        // process exit. This method is wired to IWorkspaceManager.WorkspaceClosed in the
        // constructor so closed workspace ids are dropped eagerly.
        // ConcurrentDictionary doesn't support bulk-by-key removal, so iterate. The set of
        // keys per workspace is small (one per project).
        foreach (var key in _compilations.Keys)
        {
            if (key.WorkspaceId == workspaceId)
            {
                _compilations.TryRemove(key, out _);
            }
        }

        foreach (var key in _analyzerBound.Keys)
        {
            if (key.WorkspaceId == workspaceId)
            {
                _analyzerBound.TryRemove(key, out _);
            }
        }
    }
}
