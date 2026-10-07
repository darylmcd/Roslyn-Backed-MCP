using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

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

    /// <summary>
    /// Resolves <paramref name="projectFilter"/> with the same match semantics as
    /// <see cref="FilterProjects"/>, but rejects a non-blank filter that matches no loaded
    /// project instead of returning an empty sequence. A blank filter means the whole solution.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown with <c>ParamName = "projectName"</c> when a non-blank filter matches zero projects.
    /// The fixed public message provides guidance without exposing project names or the filter.
    /// </exception>
    public static IReadOnlyList<Project> ResolveProjects(Solution solution, string? projectFilter)
    {
        var projects = FilterProjects(solution, projectFilter).ToList();
        if (projects.Count > 0 || string.IsNullOrWhiteSpace(projectFilter))
        {
            return projects;
        }

        throw new PublicArgumentException(
            "No loaded project matches parameter 'projectName'. Omit projectName or use workspace_status to list project names, then retry.",
            "projectName");
    }
}
