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
        var projects = workspaceManager.GetStatus(workspaceId).Projects;
        var project = projects
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.FilePath, projectName, StringComparison.OrdinalIgnoreCase));

        return project ?? throw NotFound(projects.Select(candidate => candidate.Name));
    }

    internal static PublicNotFoundException NotFound(IEnumerable<string> projectNames)
    {
        var available = string.Join(", ", projectNames.OrderBy(name => name, StringComparer.Ordinal));
        if (available.Length == 0) available = "(none)";
        return new PublicNotFoundException(
            $"Requested project was not found. Loaded projects: {available}. " +
            "Use workspace_status with verbose=true to inspect loaded project names, then pass a matching projectName.");
    }
}
