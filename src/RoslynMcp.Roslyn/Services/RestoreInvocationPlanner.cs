using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Snapshots package roots before restore can rewrite assets and isolates mixed roots.</summary>
internal static class RestoreInvocationPlanner
{
    internal readonly record struct ProjectRestore(string ProjectPath, string? PackagesPath);
    internal sealed record Invocation(string TargetPath, IReadOnlyList<string> Arguments);

    internal static IReadOnlyList<ProjectRestore> Capture(IEnumerable<string> projectPaths) =>
        projectPaths.Distinct(FileSystemPath.Comparer)
            .Select(path => new ProjectRestore(path, RestoreStalenessDetector.TryReadRestorePackagesPath(path)))
            .ToArray();

    internal static IReadOnlyList<string> DependencyClosure(Project project)
    {
        var graph = project.Solution.GetProjectDependencyGraph();
        var ids = graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id).Append(project.Id).ToHashSet();
        return graph.GetTopologicallySortedProjects().Where(ids.Contains)
            .Select(id => project.Solution.GetProject(id)?.FilePath
                ?? throw new InvalidOperationException("Restore requires an on-disk path for every project in the dependency graph."))
            .ToArray();
    }

    internal static IReadOnlyList<Invocation> Plan(string targetPath, IReadOnlyList<ProjectRestore> projects)
    {
        // Null is a distinct root: overriding it with another project's recorded root changes
        // that project's configured/default package folder. Linux case variants remain distinct.
        var roots = projects.Select(project => project.PackagesPath).Distinct(FileSystemPath.Comparer).ToArray();
        return roots.Length <= 1
            ? [BuildInvocation(targetPath, roots.SingleOrDefault(), noDependencies: false)]
            : projects.Select(project => BuildInvocation(project.ProjectPath, project.PackagesPath, noDependencies: true)).ToArray();
    }

    private static Invocation BuildInvocation(string targetPath, string? packagesPath, bool noDependencies)
    {
        var arguments = new List<string> { "restore", targetPath, "--nologo" };
        if (packagesPath is not null)
        {
            arguments.Add("--packages");
            arguments.Add(packagesPath);
        }
        if (noDependencies)
            arguments.Add("--no-dependencies");
        return new Invocation(targetPath, arguments);
    }
}
