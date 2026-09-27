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

    /// <summary>
    /// Resolves <paramref name="projectFilter"/> with the same match semantics as
    /// <see cref="FilterProjects"/>, but rejects a non-blank filter that matches no loaded
    /// project instead of returning an empty sequence. A blank filter means the whole solution.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown with <c>ParamName = "projectName"</c> when a non-blank filter matches zero projects.
    /// The host redacts it to the canonical compile_check "No loaded project matches" message.
    /// </exception>
    public static IReadOnlyList<Project> ResolveProjects(Solution solution, string? projectFilter)
    {
        var projects = FilterProjects(solution, projectFilter).ToList();
        if (projects.Count > 0 || string.IsNullOrWhiteSpace(projectFilter))
        {
            return projects;
        }

        var loadedProjects = solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var loadedList = loadedProjects.Count > 0
            ? string.Join(", ", loadedProjects)
            : "(none — the workspace has no projects loaded)";
        throw new ArgumentException(
            $"projectName '{projectFilter}' matched 0 projects. " +
            $"Loaded projects ({loadedProjects.Count}): {loadedList}. " +
            "Omit projectName or use workspace_status to inspect available project names.",
            "projectName");
    }
}
