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
/// <para>
/// The cache stores <see cref="Task{TResult}"/> values rather than materialized compilations,
/// so concurrent first-callers awaiting the same key share a single compilation pass instead
/// of racing to start their own. Stale entries are replaced atomically when a higher
/// workspace version is observed; we don't bother evicting older entries proactively because
/// each project has at most one slot per cache and stale tasks are released as soon as the
/// new entry is installed.
/// </para>
/// <para>
/// Because a stored task is shared by every later caller at the same workspace version, its
/// lifetime is deliberately decoupled from any one caller: the underlying work is started with
/// <see cref="CancellationToken.None"/> and each caller observes it through
/// <c>ObserveWithCallerToken</c>, which honors that caller's own token without canceling the
/// shared task. Starting the shared task with the first caller's token instead would let one
/// caller's cancellation poison the entry — every later caller would await the same
/// <see cref="TaskStatus.Canceled"/> task and see an <see cref="OperationCanceledException"/>
/// for a token that was never canceled, until the next workspace version bump replaced it.
/// Entries whose shared task completes canceled or faulted are evicted by
/// <c>EvictWhenBroken</c> so the next caller re-populates instead of replaying the failure.
/// </para>
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
        // Item #7: `compile-check-stale-assembly-refs-post-reload` — also invalidate on reload.
        // The per-read version check at lines ~54/~80 is correct in isolation but creates a
        // window where a cached `Compilation` (holding its own `MetadataReference` handles
        // through `Compilation.References`) survives until the next cache read. The fire-and-
        // forget invalidation here closes that window synchronously with the reload.
        _workspaceManager.WorkspaceReloaded += Invalidate;
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= Invalidate;
        _workspaceManager.WorkspaceReloaded -= Invalidate;
    }

    public Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var version = _workspaceManager.GetCurrentVersion(workspaceId);
        var key = (workspaceId, project.Id);

        if (_compilations.TryGetValue(key, out var existing) && existing.Version == version)
        {
            return ObserveWithCallerToken(existing.Compilation, ct);
        }

        // An already-canceled caller must not pay for — or install — a compile pass it can never
        // observe. The shared task below is deliberately started with CancellationToken.None, so
        // once it is running nothing can stop it; short-circuiting here mirrors
        // ObserveWithCallerToken's own already-canceled check and restores the pre-cache behavior
        // of handing a canceled token straight to Roslyn.
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<Compilation?>(ct);
        }

        // Cache miss or stale: install a fresh task. Multiple concurrent first-callers may
        // race here; AddOrUpdate's atomicity ensures only one entry is stored, and any racer
        // that lost will see the winner's task on the next read. The lost-racer's task will
        // run to completion but its result is discarded — acceptable cost for the simplicity
        // of not holding a per-key lock.
        //
        // CancellationToken.None is deliberate: this task becomes shared state for every later
        // caller at this version, so it must not inherit whichever caller happened to arrive
        // first. The caller's own token is applied to the returned wrapper instead.
        var compilationTask = project.GetCompilationAsync(CancellationToken.None);
        var entry = new CompilationEntry(version, compilationTask);
        _compilations.AddOrUpdate(key, entry, (_, current) => current.Version >= version ? current : entry);
        // Registered after AddOrUpdate so an already-completed broken task still evicts the
        // entry we just installed rather than racing ahead of the install.
        EvictWhenBroken(_compilations, key, entry, entry.Compilation);

        // Return whichever entry actually won the AddOrUpdate so all callers observe the
        // same task instance.
        var shared = _compilations.TryGetValue(key, out var winner) && winner.Version == version
            ? winner.Compilation
            : compilationTask;
        return ObserveWithCallerToken(shared, ct);
    }

    public async Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var version = _workspaceManager.GetCurrentVersion(workspaceId);
        var key = (workspaceId, project.Id);

        if (_analyzerBound.TryGetValue(key, out var existing) && existing.Version == version)
        {
            return await ObserveWithCallerToken(existing.Bound, ct).ConfigureAwait(false);
        }

        // Same shared-task discipline as GetCompilationAsync: the analyzer-bound task is started
        // detached from any caller's token, and the nested GetCompilationAsync it performs is
        // therefore detached too.
        var task = BuildCompilationWithAnalyzersAsync(workspaceId, project);
        var entry = new AnalyzerEntry(version, task);
        _analyzerBound.AddOrUpdate(key, entry, (_, current) => current.Version >= version ? current : entry);
        EvictWhenBroken(_analyzerBound, key, entry, entry.Bound);

        var winner = _analyzerBound.TryGetValue(key, out var stored) && stored.Version == version
            ? stored.Bound
            : task;
        return await ObserveWithCallerToken(winner, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the calling caller's <see cref="CancellationToken"/> to a shared cached task
    /// without canceling that task for anyone else. An already-canceled token short-circuits so
    /// the caller sees <see cref="OperationCanceledException"/> deterministically, matching the
    /// pre-cache behavior of passing a canceled token straight to Roslyn.
    /// </summary>
    private static Task<T> ObserveWithCallerToken<T>(Task<T> shared, CancellationToken ct)
    {
        if (!ct.CanBeCanceled) return shared;
        if (ct.IsCancellationRequested) return Task.FromCanceled<T>(ct);
        return shared.WaitAsync(ct);
    }

    /// <summary>
    /// Removes <paramref name="entry"/> from <paramref name="map"/> if its shared task ends up
    /// canceled or faulted, so the next caller re-populates instead of replaying the failure
    /// until the workspace version bumps. The removal is a compare-and-remove against the exact
    /// entry (both <c>CompilationEntry</c> and <c>AnalyzerEntry</c> are records with structural
    /// equality over version + task identity), so a newer entry installed concurrently by a
    /// version bump is never collateral damage.
    /// </summary>
    private static void EvictWhenBroken<TEntry>(
        ConcurrentDictionary<(string WorkspaceId, ProjectId ProjectId), TEntry> map,
        (string WorkspaceId, ProjectId ProjectId) key,
        TEntry entry,
        Task shared)
        where TEntry : notnull
    {
        shared.ContinueWith(
            completed =>
            {
                // Mark a faulted shared task observed; callers that already bailed out via their
                // own token would otherwise leave it unobserved.
                _ = completed.Exception;
                ((ICollection<KeyValuePair<(string WorkspaceId, ProjectId ProjectId), TEntry>>)map)
                    .Remove(new KeyValuePair<(string WorkspaceId, ProjectId ProjectId), TEntry>(key, entry));
            },
            CancellationToken.None,
            TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<CompilationWithAnalyzers?> BuildCompilationWithAnalyzersAsync(
        string workspaceId, Project project)
    {
        var compilation = await GetCompilationAsync(workspaceId, project, CancellationToken.None).ConfigureAwait(false);
        if (compilation is null) return null;

        // unresolved-analyzer-reference-crash: WorkspaceManager.StripUnresolvedAnalyzerReferences
        // removes UnresolvedAnalyzerReference entries at load time, so this site no longer needs
        // its own filter. The Where clause was previously the FLAG-A workaround.
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
