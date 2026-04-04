using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using RoslynProject = Microsoft.CodeAnalysis.Project;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectMetadataParser
{
    public static IReadOnlyList<string> GetTargetFrameworks(RoslynProject project, XDocument? document, ILogger? logger = null)
    {
        var evaluatedFrameworks = GetEvaluatedTargetFrameworks(project.FilePath, logger);
        return evaluatedFrameworks.Count > 0 ? evaluatedFrameworks : GetTargetFrameworks(document);
    }

    public static IReadOnlyList<string> GetTargetFrameworks(XDocument? document)
    {
        if (document is null)
        {
            return ["unknown"];
        }

        var targetFramework = document.Descendants("TargetFramework").Select(element => element.Value.Trim());
        var targetFrameworks = document.Descendants("TargetFrameworks")
            .SelectMany(element => element.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var allFrameworks = targetFramework.Concat(targetFrameworks).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return allFrameworks.Count > 0 ? allFrameworks : ["unknown"];
    }

    private static IReadOnlyList<string> GetEvaluatedTargetFrameworks(string? projectFilePath, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return [];
        }

        var projectCollection = new ProjectCollection();

        try
        {
            var evaluatedProject = projectCollection.LoadProject(projectFilePath);
            var frameworks = ParseTargetFrameworkValues(
                evaluatedProject.GetPropertyValue("TargetFramework"),
                evaluatedProject.GetPropertyValue("TargetFrameworks"));

            return frameworks;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to evaluate target frameworks for project {Path}", projectFilePath);
            return [];
        }
        finally
        {
            projectCollection.UnloadAllProjects();
        }
    }

    private static IReadOnlyList<string> ParseTargetFrameworkValues(string? targetFramework, string? targetFrameworks)
    {
        var singleFrameworks = string.IsNullOrWhiteSpace(targetFramework)
            ? Array.Empty<string>()
            : new[] { targetFramework.Trim() };
        var multiFrameworks = string.IsNullOrWhiteSpace(targetFrameworks)
            ? Array.Empty<string>()
            : targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return singleFrameworks
            .Concat(multiFrameworks)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly HashSet<string> TestPackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "xunit", "xunit.core", "xunit.v3", "xunit.v3.core",
        "NUnit", "nunit", "NUnit3TestAdapter",
        "MSTest.TestFramework", "Microsoft.NET.Test.Sdk",
    };

    public static bool IsTestProject(XDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        // Explicit <IsTestProject>true</IsTestProject> (MSTest SDK sets this automatically)
        var isTestProject = document.Descendants("IsTestProject").FirstOrDefault()?.Value;
        if (bool.TryParse(isTestProject, out var parsed) && parsed)
        {
            return true;
        }

        // Heuristic: check for test framework package references (xUnit, NUnit, MSTest)
        var packageReferences = document.Descendants("PackageReference");
        foreach (var pkgRef in packageReferences)
        {
            var include = pkgRef.Attribute("Include")?.Value;
            if (include is not null && TestPackageNames.Contains(include))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the Roslyn project is a test project (<c>IsTestProject</c> or test framework packages).
    /// </summary>
    public static bool IsTestProject(RoslynProject project)
    {
        var doc = LoadProjectDocument(project.FilePath, null);
        return IsTestProject(doc);
    }

    public static string GetOutputType(XDocument? document)
    {
        return document?.Descendants("OutputType").FirstOrDefault()?.Value.Trim() ?? "Library";
    }

    public static string GetAssemblyName(RoslynProject project)
    {
        if (project.CompilationOptions?.AssemblyIdentityComparer is not null && !string.IsNullOrWhiteSpace(project.AssemblyName))
        {
            return project.AssemblyName;
        }

        return project.Name;
    }

    public static XDocument? LoadProjectDocument(string? projectFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return null;
        }

        try
        {
            return XDocument.Load(projectFilePath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse project file {Path}", projectFilePath);
            return null;
        }
    }
}
