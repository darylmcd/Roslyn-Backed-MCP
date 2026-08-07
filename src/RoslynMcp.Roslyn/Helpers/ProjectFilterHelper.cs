using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectFilterHelper
{
    public static IEnumerable<Project> FilterProjects(Solution solution, string? projectFilter)
    {
        return string.IsNullOrWhiteSpace(projectFilter)
            ? solution.Projects
            : solution.Projects.Where(p =>
                string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase));
    }
}
