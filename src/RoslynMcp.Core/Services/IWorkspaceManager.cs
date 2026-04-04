using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Manages the lifecycle of Roslyn <c>MSBuildWorkspace</c> sessions, providing load, reload,
/// query, and mutation operations over named workspace instances.
/// </summary>
public interface IWorkspaceManager
{
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
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when no session with <paramref name="workspaceId"/> exists.</exception>
    WorkspaceStatusDto GetStatus(string workspaceId);

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
    /// Attempts to apply a modified <see cref="Solution"/> back to the workspace.
    /// </summary>
    /// <returns><see langword="true"/> if the changes were accepted; <see langword="false"/> if the workspace rejected them.</returns>
    bool TryApplyChanges(string workspaceId, Solution newSolution);
}
