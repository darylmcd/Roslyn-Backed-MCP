using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectGraphHelpers
{
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
