using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectGraphHelpers
{
    internal static Solution EnsureProjectReference(Solution solution, ProjectId sourceProjectId, ProjectId targetProjectId)
    {
        var sourceProject = solution.GetProject(sourceProjectId)
            ?? throw new InvalidOperationException("Source project was not found in the updated solution.");
        if (sourceProjectId == targetProjectId) return solution;

        var existing = sourceProject.ProjectReferences.FirstOrDefault(reference => reference.ProjectId == targetProjectId);
        if (existing is not null)
        {
            if (existing.Aliases.IsDefaultOrEmpty || existing.Aliases.Contains("global", StringComparer.Ordinal))
                return solution;
            var visible = new ProjectReference(targetProjectId, existing.Aliases.Add("global"), existing.EmbedInteropTypes);
            return solution.WithProjectReferences(sourceProjectId,
                sourceProject.ProjectReferences.Select(reference => reference == existing ? visible : reference));
        }

        var targetProject = solution.GetProject(targetProjectId)
            ?? throw new InvalidOperationException("Target project was not found in the updated solution.");

        if (ProjectGraphHelpers.WouldCreateProjectReferenceCycle(sourceProject, targetProject))
        {
            throw new PublicInvalidOperationException(
                $"Adding project reference from '{sourceProject.Name}' to '{targetProject.Name}' would create a circular dependency.");
        }

        return solution.AddProjectReference(sourceProjectId, new ProjectReference(targetProjectId));
    }

    public static bool WouldCreateProjectReferenceCycle(Project source, Project target)
    {
        var visited = new HashSet<ProjectId>();
        var stack = new Stack<Project>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            if (current.Id == source.Id)
            {
                return true;
            }

            foreach (var reference in current.ProjectReferences)
            {
                var next = current.Solution.GetProject(reference.ProjectId);
                if (next is not null)
                {
                    stack.Push(next);
                }
            }
        }

        return false;
    }
}
