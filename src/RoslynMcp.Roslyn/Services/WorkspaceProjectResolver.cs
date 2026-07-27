using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Resolves a project status by project name or project-file path.
/// </summary>
internal static class WorkspaceProjectResolver
{
    public static ProjectStatusDto Resolve(
        IWorkspaceManager workspaceManager,
        string workspaceId,
        string projectName)
    {
        var project = workspaceManager.GetStatus(workspaceId).Projects
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.FilePath, projectName, StringComparison.OrdinalIgnoreCase));

        return project ?? throw new InvalidOperationException(
            $"Project '{projectName}' was not found in workspace '{workspaceId}'.");
    }
}
