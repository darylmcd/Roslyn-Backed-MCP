using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectFilterHelper
{
    public static IEnumerable<Project> FilterProjects(Solution solution, string? projectFilter)
    {
        return projectFilter is null
            ? solution.Projects
            : solution.Projects.Where(p =>
                string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase));
    }
}
