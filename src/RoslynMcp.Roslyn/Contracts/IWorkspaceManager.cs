using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// Manages the lifecycle of Roslyn <c>MSBuildWorkspace</c> sessions, providing load, reload,
/// query, and mutation operations over named workspace instances.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// Raised when a workspace session is closed (via <see cref="Close"/> or process disposal).
    /// Subscribers receive the closed workspace's identifier and can use it to free their own
    /// per-workspace state. Handlers must not throw; the manager will swallow exceptions to keep
    /// the close path reliable.
    /// </summary>
    event Action<string>? WorkspaceClosed;

    /// <summary>
    /// Raised after <see cref="ReloadAsync"/> has successfully replaced the underlying
    /// <c>MSBuildWorkspace</c> and bumped the workspace version. Subscribers receive the
    /// reloaded workspace's identifier.
    /// </summary>
    /// <remarks>
    /// Item #7 (<c>compile-check-stale-assembly-refs-post-reload</c>) — version-keyed caches
    /// already invalidate themselves on the next read when the workspace version changes, but
    /// some Roslyn internals hold references that survive across the <c>OpenSolutionAsync</c>
    /// refresh. Exposing an explicit reload signal lets caches drop their entries synchronously
    /// with the reload, eliminating the small-but-real window where a tool can observe a
    /// stale cached <see cref="Microsoft.CodeAnalysis.Compilation"/> (and its associated
    /// <see cref="Microsoft.CodeAnalysis.MetadataReference"/> handles) between reload
    /// completion and the version check. Handlers must not throw; the manager swallows
    /// handler exceptions so a misbehaving subscriber cannot break the reload path.
    /// </remarks>
    event Action<string>? WorkspaceReloaded;

    /// <summary>
    /// Loads a solution or project file from disk into a new named workspace session.
    /// </summary>
    /// <param name="path">The absolute path to the <c>.sln</c>, <c>.slnx</c>, or <c>.csproj</c> file to load.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct);

    /// <summary>
    /// Reloads an existing workspace session from disk, discarding any pending previews.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier to reload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct);

    /// <summary>
    /// Returns whether an active workspace session exists for the given identifier.
    /// </summary>
    bool ContainsWorkspace(string workspaceId);

    /// <summary>
    /// Returns whether the specified workspace has observed on-disk changes since its
    /// last load or reload. Used by <see cref="Services.WorkspaceExecutionGate"/> to gate
    /// tool calls against stale workspace state (see <c>ROSLYNMCP_ON_STALE</c>).
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <returns><see langword="true"/> when the underlying file watcher has flagged stale; <see langword="false"/> otherwise (including when the workspace does not exist).</returns>
    bool IsStale(string workspaceId);

    /// <summary>
    /// Closes the specified workspace session and releases its resources.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier to close.</param>
    /// <returns><see langword="true"/> if the session was found and closed; <see langword="false"/> if no session with the given identifier exists.</returns>
    bool Close(string workspaceId);

    /// <summary>
    /// Returns status information for all currently active workspace sessions.
    /// </summary>
    IReadOnlyList<WorkspaceStatusDto> ListWorkspaces();

    /// <summary>
    /// Returns the current status for the specified workspace session.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="GetStatusAsync"/> when the call site can await — it uses the workspace load lock asynchronously
    /// instead of blocking a thread while the lock is held.
    /// </remarks>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when no session with <paramref name="workspaceId"/> exists.</exception>
    WorkspaceStatusDto GetStatus(string workspaceId);

    /// <summary>
    /// Returns the current status for the specified workspace session, waiting on the same load lock as
    /// <see cref="GetStatus"/> without blocking a thread while the lock is held.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when no session with <paramref name="workspaceId"/> exists.</exception>
    Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the project and dependency graph for the specified workspace session.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    ProjectGraphDto GetProjectGraph(string workspaceId);

    /// <summary>
    /// Returns the source-generated documents for the workspace or a specific project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectName">Restricts results to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct);

    /// <summary>
    /// Returns the current source text for the given file as Roslyn sees it in the loaded workspace.
    /// Returns <see langword="null"/> if the file is not part of the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct);

    /// <summary>
    /// Returns the current incremental version number of the workspace session.
    /// The version is bumped on each successful reload.
    /// </summary>
    int GetCurrentVersion(string workspaceId);

    /// <summary>
    /// Returns the current Roslyn <see cref="Solution"/> for the workspace session.
    /// </summary>
    Solution GetCurrentSolution(string workspaceId);

    /// <summary>
    /// Resolves a project by name or absolute file path within the workspace, using a
    /// per-<c>workspaceVersion</c> index so every callsite (services, helpers) shares one
    /// O(1) lookup instead of scanning <see cref="Solution.Projects"/> each call. Lookup is
    /// case-insensitive on both name and file path. Returns <see langword="null"/> when the
    /// project is not found — callers should wrap with their own throw to keep error context.
    /// </summary>
    Project? GetProject(string workspaceId, string projectNameOrPath);

    /// <summary>
    /// Attempts to apply a modified <see cref="Solution"/> back to the workspace.
    /// </summary>
    /// <returns><see langword="true"/> if the changes were accepted; <see langword="false"/> if the workspace rejected them.</returns>
    bool TryApplyChanges(string workspaceId, Solution newSolution);

    /// <summary>
    /// Restores the workspace version counter to a previously captured value.
    /// Used after a revert/undo operation to allow preview tokens that were valid before
    /// the reverted apply to remain valid, since the workspace state has been restored
    /// to its pre-apply form.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="version">The version to restore (typically captured before the apply being reverted).</param>
    void RestoreVersion(string workspaceId, int version);
}
