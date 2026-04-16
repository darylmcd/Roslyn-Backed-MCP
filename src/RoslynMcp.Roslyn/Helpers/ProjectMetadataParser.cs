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

        MsBuildInitializer.EnsureInitialized();
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

    /// <summary>
    /// Item #1 / severity-critical-fail-preview-diff-does-not-match-t. Computes the relative
    /// folder segments from a project file to a target file, for use as the <c>folders</c>
    /// argument to <see cref="Microsoft.CodeAnalysis.Project.AddDocument(string, Microsoft.CodeAnalysis.Text.SourceText, System.Collections.Generic.IEnumerable{string}?, string?, System.Collections.Generic.IReadOnlyList{string}?, bool)"/>.
    /// Omitting folders makes MSBuildWorkspace treat the added document as living at project
    /// root, which — when our own explicit disk write uses a deeper path — produces TWO files
    /// on disk (the deep path plus a rogue project-root copy). All AddDocument callers that
    /// target a file outside the project root MUST pass folders to keep Roslyn's path
    /// resolution consistent with the explicit write.
    /// </summary>
    /// <param name="projectFilePath">The absolute path to the .csproj.</param>
    /// <param name="filePath">The absolute path to the file being added.</param>
    /// <returns>
    /// The relative folder segments from the project directory to the file directory,
    /// or an empty list when the file lives at project root or when paths are not resolvable.
    /// </returns>
    public static IReadOnlyList<string> ComputeDocumentFolders(string? projectFilePath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return [];
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(fileDirectory))
        {
            return [];
        }

        var relativeDirectory = Path.GetRelativePath(projectDirectory, fileDirectory);
        if (string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return [];
        }

        return relativeDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && folder != ".")
            .ToArray();
    }

    /// <summary>
    /// Item #5 — <c>severity-medium-breaks-msbuild-until-csproj-is-hand</c>.
    /// Returns <see langword="true"/> when the project file is SDK-style
    /// (has an <c>Sdk=</c> attribute on the root <c>&lt;Project&gt;</c> element
    /// or a top-level <c>&lt;Sdk&gt;</c> import) AND does not explicitly disable
    /// default compile items with <c>&lt;EnableDefaultCompileItems&gt;false&lt;/EnableDefaultCompileItems&gt;</c>.
    /// </summary>
    /// <remarks>
    /// When this returns true, the server MUST NOT let
    /// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.TryApplyChanges(Microsoft.CodeAnalysis.Solution)"/>
    /// inject an explicit <c>&lt;Compile Include="…"/&gt;</c> for an added document —
    /// the default glob picks up the file automatically on the next workspace load, and
    /// an explicit include produces the <c>Duplicate 'Compile' items were included</c>
    /// MSBuild error reported in the firewall-analyzer audit §9.6 (BUG-COMPILE-INCLUDE)
    /// and IT-Chat-Bot audit §9.1.
    /// </remarks>
    public static bool IsSdkStyleWithDefaultCompileItems(string? projectFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        var document = LoadProjectDocument(projectFilePath, logger);
        return IsSdkStyleWithDefaultCompileItems(document);
    }

    /// <summary>
    /// XML-shape overload — avoids re-reading the file when the caller already has a
    /// parsed document. Pure: no I/O, no MSBuild evaluation.
    /// </summary>
    public static bool IsSdkStyleWithDefaultCompileItems(XDocument? document)
    {
        if (document?.Root is null)
        {
            return false;
        }

        var root = document.Root;

        // SDK-style detection. Three shapes in the wild:
        //   1. Attribute form: <Project Sdk="Microsoft.NET.Sdk">…</Project>    (most common)
        //   2. Element form:   <Project><Sdk Name="Microsoft.NET.Sdk"/>…</Project>
        //   3. Import form:    <Project><Import Sdk="…"/>…</Project>
        // Non-SDK legacy csprojs use <Project ToolsVersion=…> without Sdk attributes.
        var hasSdkAttribute = root.Attribute("Sdk") is not null;
        var hasSdkElement = root.Elements().Any(e => string.Equals(e.Name.LocalName, "Sdk", StringComparison.Ordinal));
        var hasSdkImport = root.Descendants().Any(e =>
            string.Equals(e.Name.LocalName, "Import", StringComparison.Ordinal) &&
            e.Attribute("Sdk") is not null);

        var isSdkStyle = hasSdkAttribute || hasSdkElement || hasSdkImport;
        if (!isSdkStyle)
        {
            return false;
        }

        // Default is true for SDK-style; only opted-out projects carry
        // <EnableDefaultCompileItems>false</EnableDefaultCompileItems> explicitly.
        // We intentionally only look at XML; if a user overrides the property via an
        // imported .props file they're taking responsibility for their build graph.
        foreach (var element in root.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "EnableDefaultCompileItems", StringComparison.Ordinal))
            {
                continue;
            }

            if (bool.TryParse(element.Value.Trim(), out var enabled) && !enabled)
            {
                return false;
            }
        }

        return true;
    }
}
