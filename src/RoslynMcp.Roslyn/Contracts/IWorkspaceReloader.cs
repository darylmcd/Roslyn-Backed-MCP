using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>
/// The reload capability of <see cref="IWorkspaceManager"/>, split out so a caller that must
/// reload a workspace it does not already hold the writer lock for can substitute a
/// writer-locked implementation (see <c>WorkspaceTools.LoadWorkspace</c>'s restore reload).
/// </summary>
public interface IWorkspaceReloader
{
    /// <summary>
    /// Reloads an existing workspace session from disk, discarding any pending previews.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier to reload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct);
}
