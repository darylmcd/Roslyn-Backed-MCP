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
    /// Returns the cached <see cref="Compilation"/> for the given project, or computes and caches it.
    /// </summary>
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
