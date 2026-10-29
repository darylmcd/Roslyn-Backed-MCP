using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectGraphHelpers
{
    /// <summary>
    /// True when <paramref name="consumer"/>'s compilation already sees <paramref name="target"/>'s assembly
    /// (a direct reference, or one that flows in through its own reference list). A compilation check, not a
    /// graph walk: Roslyn compiles against a project's direct references only.
    /// </summary>
    public static async Task<bool> CompilationReferencesProjectAsync(Project consumer, Project target, CancellationToken ct)
    {
        if (consumer.Id == target.Id)
        {
            return true;
        }

        var compilation = await consumer.GetCompilationAsync(ct).ConfigureAwait(false);
        var targetAssembly = target.AssemblyName;
        return compilation is not null &&
               compilation.ReferencedAssemblyNames.Any(name => string.Equals(name.Name, targetAssembly, StringComparison.Ordinal));
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
