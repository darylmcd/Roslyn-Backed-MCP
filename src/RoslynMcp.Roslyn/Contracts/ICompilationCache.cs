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
    /// Drops every cached compilation for a workspace. Called by
    /// <see cref="IWorkspaceManager"/> when the workspace is closed.
    /// </summary>
    void Invalidate(string workspaceId);
}
