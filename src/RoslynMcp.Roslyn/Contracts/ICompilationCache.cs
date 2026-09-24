using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// Per-workspace, version-keyed cache for Roslyn <see cref="Compilation"/> and
/// <see cref="CompilationWithAnalyzers"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Compilations are the most expensive object Roslyn produces — many analysis tools repeat
/// the same <c>project.GetCompilationAsync()</c> call across requests, throwing the result
/// away each time. This cache lets independent services share warm compilations as long as
/// the workspace version is unchanged.
/// </para>
/// <para>
/// Cache invalidation is keyed on the monotonic <see cref="IWorkspaceManager.GetCurrentVersion"/>
/// counter. <see cref="IWorkspaceManager.TryApplyChanges"/> bumps the version on every successful
/// apply, and a workspace reload bumps it as well, so any mutation transparently invalidates
/// previously cached compilations. Workspace close calls <see cref="Invalidate"/> to free
/// the dictionary slots.
/// </para>
/// <para>
/// Implementations must be safe for concurrent use. The first caller for a
/// <c>(workspaceId, projectId, version)</c> tuple starts the underlying compilation; subsequent
/// concurrent callers await the same in-flight task instead of racing.
/// </para>
/// <para>
/// Cancellation is per-caller, never per-entry. The token a caller passes to
/// <see cref="GetCompilationAsync"/> or <see cref="GetCompilationWithAnalyzersAsync"/> cancels
/// only that caller's own await of the shared entry: it must not cancel the shared compilation
/// pass itself, and it must not affect any other caller reading the same cache slot at the same
/// workspace version. A caller whose token is already canceled on entry observes
/// <see cref="OperationCanceledException"/> from both methods, and both guarantee that no
/// compilation pass — raw or analyzer-bound — is started and no entry is installed for such a
/// caller. Conversely, an entry whose shared work ends up canceled or faulted must be
/// dropped so the next caller re-populates it instead of replaying the failure until the next
/// workspace version bump.
/// </para>
/// </remarks>
public interface ICompilationCache
{
    /// <summary>
    /// DIAGNOSTIC-ONLY. Returns a cached compilation produced by re-running the project's source
    /// generators, plus the generator-driver diagnostics that
    /// <see cref="Compilation.GetDiagnostics(CancellationToken)"/> does not include.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="CompilationSnapshot.Compilation"/> is NOT owned by the project's
    /// <see cref="Solution"/>: its generated syntax trees are fresh instances. Symbols taken from it
    /// resolve zero references through <c>SymbolFinder</c> against the solution, so it must feed
    /// diagnostic reporting only. Every symbol consumer uses <see cref="GetCompilationAsync"/>.
    /// Implementations that do not execute generators explicitly retain source compatibility
    /// through this default projection.
    /// </remarks>
    async Task<CompilationSnapshot?> GetCompilationSnapshotAsync(
        string workspaceId,
        Project project,
        CancellationToken ct)
    {
        var compilation = await GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
        return compilation is null
            ? null
            : new CompilationSnapshot(compilation, ImmutableArray<Diagnostic>.Empty);
    }

    /// <summary>
    /// Returns the cached Solution-owned <see cref="Compilation"/> for the given project (the
    /// instance <c>project.GetCompilationAsync()</c> yields), or computes and caches it.
    /// </summary>
    /// <remarks>
    /// This is the only compilation a symbol consumer may use: a symbol passed to
    /// <c>SymbolFinder</c> with the project's <see cref="Solution"/> must come from a compilation
    /// that solution owns, or it resolves zero references. Diagnostic surfaces that need
    /// generator-rerun parity use <see cref="GetCompilationSnapshotAsync"/> instead.
    /// </remarks>
    /// <param name="workspaceId">The workspace session identifier the project belongs to.</param>
    /// <param name="project">The Roslyn project whose compilation is requested.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct);

    /// <summary>
    /// Returns the cached <see cref="CompilationWithAnalyzers"/> for the given project, or
    /// computes and caches it. Returns <see langword="null"/> if the project has no analyzers
    /// configured or its compilation cannot be obtained.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier the project belongs to.</param>
    /// <param name="project">The Roslyn project whose analyzer-bound compilation is requested.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(string workspaceId, Project project, CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="solution"/> is reference-equal to the
    /// solution <see cref="IWorkspaceManager.GetCurrentSolution"/> currently reports for
    /// <paramref name="workspaceId"/> — i.e. it is safe to serve that solution's per-project
    /// compilations from this <c>(workspaceId, projectId, version)</c>-keyed cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the liveness gate the read-side helpers
    /// (<c>SymbolResolver.CanUseCompilationCache</c>) delegate to. Two distinct failure modes are
    /// both rejected by it: a FORKED solution (e.g. <c>solution.WithDocumentText(...)</c>, or a
    /// never-applied preview solution) whose document text differs from the live workspace content,
    /// and a solution belonging to a DIFFERENT workspace than <paramref name="workspaceId"/> names
    /// — the latter cannot be detected from the solution's own <see cref="Solution.Workspace"/>
    /// back-reference alone, which is why the check lives here (where the
    /// <see cref="IWorkspaceManager"/> is available) rather than in the static helper.
    /// </para>
    /// <para>
    /// Implementations must not throw for a workspace that is mid-reload or whose snapshot was
    /// disposed by a concurrent reload; they return <see langword="false"/> so the caller degrades
    /// to a raw <c>project.GetCompilationAsync</c> fetch instead of failing a read-side query. An
    /// unknown <paramref name="workspaceId"/> is a caller bug and still surfaces as an exception,
    /// matching <see cref="GetCompilationAsync"/>'s own behavior.
    /// </para>
    /// <para>
    /// Callers must re-evaluate this per compilation fetch, not once before a project loop: a
    /// workspace reload landing mid-scan bumps the version, and a stale-solution fetch made after
    /// the bump would compute-and-store a pre-bump compilation under the post-bump cache key.
    /// </para>
    /// </remarks>
    /// <param name="workspaceId">The workspace session identifier the solution is claimed to belong to.</param>
    /// <param name="solution">The solution the caller intends to read compilations from.</param>
    bool IsLiveSolution(string workspaceId, Solution solution);

    /// <summary>
    /// Drops every cached compilation for a workspace. Called by
    /// <see cref="IWorkspaceManager"/> when the workspace is closed.
    /// </summary>
    void Invalidate(string workspaceId);
}

/// <summary>
/// A source-generator-complete compiler snapshot and the generator-driver diagnostics that
/// are not included in <see cref="Compilation.GetDiagnostics(CancellationToken)"/>.
/// </summary>
public sealed record CompilationSnapshot(
    Compilation Compilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics);
