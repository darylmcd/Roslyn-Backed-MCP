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
/// The cache stores lazily-created <see cref="Task{TResult}"/> values rather than materialized
/// compilations, so concurrent first-callers install a winner before any compilation starts and
/// then share that single pass. Stale entries are replaced atomically when a higher
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
    private readonly Func<Project, Task<Compilation?>> _compilationFactory;
    private readonly Func<string, Project, Task<CompilationWithAnalyzers?>> _analyzerFactory;

    private sealed record CompilationEntry(int Version, Lazy<Task<Compilation?>> Compilation);
    private sealed record AnalyzerEntry(int Version, Lazy<Task<CompilationWithAnalyzers?>> Bound);

    // Two parallel maps. Splitting them lets a service that only needs the raw Compilation
    // (e.g., dead-code analysis) skip warming the analyzer pipeline.
    private readonly ConcurrentDictionary<(string WorkspaceId, ProjectId ProjectId), CompilationEntry> _compilations = new();
    private readonly ConcurrentDictionary<(string WorkspaceId, ProjectId ProjectId), AnalyzerEntry> _analyzerBound = new();

    public CompilationCache(IWorkspaceManager workspaceManager)
        : this(workspaceManager, compilationFactory: null, analyzerFactory: null)
    {
    }

    internal CompilationCache(
        IWorkspaceManager workspaceManager,
        Func<Project, Task<Compilation?>>? compilationFactory,
        Func<string, Project, Task<CompilationWithAnalyzers?>>? analyzerFactory)
    {
        _workspaceManager = workspaceManager;
        _compilationFactory = compilationFactory
            ?? (static project => project.GetCompilationAsync(CancellationToken.None));
        _analyzerFactory = analyzerFactory ?? BuildCompilationWithAnalyzersAsync;
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
            return ObserveWithCallerToken(existing.Compilation.Value, ct);
        }

        // An already-canceled caller must not pay for — or install — a compile pass it can never
        // observe. The shared task below is deliberately started with CancellationToken.None, so
        // once it is running nothing can stop it; short-circuiting here mirrors
        // ObserveWithCallerToken's own already-canceled check and restores the pre-cache behavior
        // of handing a canceled token straight to Roslyn.
        ct.ThrowIfCancellationRequested();

        // Lazy is load-bearing: AddOrUpdate may invoke its factories more than once, so starting
        // Roslyn work while constructing a candidate would still let losing racers compile.
        // Only the entry returned by AddOrUpdate has Value evaluated.
        var candidate = new CompilationEntry(
            version,
            new Lazy<Task<Compilation?>>(
                () => _compilationFactory(project),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = _compilations.AddOrUpdate(
            key,
            candidate,
            (_, current) => current.Version >= version ? current : candidate);
        var shared = entry.Version == version
            ? entry.Compilation.Value
            : candidate.Compilation.Value;
        EvictWhenBroken(_compilations, key, entry.Version == version ? entry : candidate, shared);
        return ObserveWithCallerToken(shared, ct);
    }

    public async Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var version = _workspaceManager.GetCurrentVersion(workspaceId);
        var key = (workspaceId, project.Id);

        if (_analyzerBound.TryGetValue(key, out var existing) && existing.Version == version)
        {
            return await ObserveWithCallerToken(existing.Bound.Value, ct).ConfigureAwait(false);
        }

        // An already-canceled caller must not pay for — or install — an analyzer-bound build pass
        // it can never observe. The shared task below is started detached via
        // CancellationToken.None, so once it is running nothing can stop it.
        ct.ThrowIfCancellationRequested();

        var candidate = new AnalyzerEntry(
            version,
            new Lazy<Task<CompilationWithAnalyzers?>>(
                () => _analyzerFactory(workspaceId, project),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = _analyzerBound.AddOrUpdate(
            key,
            candidate,
            (_, current) => current.Version >= version ? current : candidate);
        var shared = entry.Version == version
            ? entry.Bound.Value
            : candidate.Bound.Value;
        EvictWhenBroken(_analyzerBound, key, entry.Version == version ? entry : candidate, shared);
        return await ObserveWithCallerToken(shared, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// group-c-compilation-cache-gate-hardening: authoritative liveness check for the read-side
    /// opt-in gate. Reference-equality against <see cref="IWorkspaceManager.GetCurrentSolution"/>
    /// for the CALLER-SUPPLIED <paramref name="workspaceId"/> — not against the solution's own
    /// <see cref="Solution.Workspace"/> back-reference, which cannot distinguish a live solution
    /// belonging to a different workspace from one belonging to this cache slot's workspace.
    /// </summary>
    public bool IsLiveSolution(string workspaceId, Solution solution)
    {
        if (solution is null)
        {
            return false;
        }

        try
        {
            return ReferenceEquals(solution, _workspaceManager.GetCurrentSolution(workspaceId));
        }
        catch (StaleWorkspaceTransitionException)
        {
            // The workspace is mid-reload (or its last reload failed) and has no valid snapshot to
            // compare against. Degrade to the raw per-project fetch rather than throwing out of a
            // read-side helper — mirrors the ObjectDisposedException defense this check replaced.
            return false;
        }
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
