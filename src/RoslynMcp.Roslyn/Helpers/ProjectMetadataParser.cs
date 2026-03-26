using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectMetadataParser
{
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

    public static bool IsTestProject(XDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        var isTestProject = document.Descendants("IsTestProject").FirstOrDefault()?.Value;
        return bool.TryParse(isTestProject, out var parsed) && parsed;
    }

    public static string GetOutputType(XDocument? document)
    {
        return document?.Descendants("OutputType").FirstOrDefault()?.Value.Trim() ?? "Library";
    }

    public static string GetAssemblyName(Project project)
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
