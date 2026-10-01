using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Locates and reads MSBuild metadata files (<c>Directory.Packages.props</c>,
/// <c>Directory.Build.props</c>) by walking the directory hierarchy.
/// </summary>
internal static class MsBuildMetadataHelper
{
    /// <summary>
    /// Evaluates <paramref name="projectFilePath"/> with MSBuild in a throwaway
    /// <see cref="ProjectCollection"/> and returns the evaluated value of each requested property
    /// (empty string when unset), in the order of <paramref name="propertyNames"/>. Evaluation
    /// failures propagate (an <c>InvalidProjectFileException</c> as <see cref="InvalidOperationException"/>,
    /// I/O as-is) so each caller picks its own degradation policy.
    /// </summary>
    public static string[] EvaluateProperties(string projectFilePath, params string[] propertyNames)
    {
        // The MSBuild types live in a separate non-inlined method: the JIT resolves
        // Microsoft.Build when it compiles a method that mentions those types, which must happen
        // only after EnsureInitialized has registered the MSBuild assembly resolver.
        MsBuildInitializer.EnsureInitialized();
        return EvaluatePropertiesCore(projectFilePath, propertyNames);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] EvaluatePropertiesCore(string projectFilePath, string[] propertyNames)
    {
        var projectCollection = new ProjectCollection();
        try
        {
            var evaluated = projectCollection.LoadProject(projectFilePath);
            return propertyNames.Select(evaluated.GetPropertyValue).ToArray();
        }
        catch (InvalidProjectFileException ex)
        {
            // Surfaced as InvalidOperationException so callers never name a Microsoft.Build type
            // in a catch clause: the JIT would resolve it before the MSBuild resolver is registered.
            throw new InvalidOperationException($"MSBuild could not evaluate '{projectFilePath}': {ex.Message}", ex);
        }
        finally
        {
            projectCollection.UnloadAllProjects();
        }
    }

    /// <summary>
    /// Searches the directory tree from <paramref name="loadedPath"/> upward for
    /// the nearest <c>Directory.Packages.props</c> file.
    /// </summary>
    /// <returns>The absolute path to the file, or <see langword="null"/> if not found.</returns>
    public static string? FindDirectoryPackagesProps(string? loadedPath)
    {
        return FindNearestFile(loadedPath, "Directory.Packages.props");
    }

    /// <summary>
    /// Returns <see langword="true"/> if the given <c>Directory.Packages.props</c> file has
    /// <c>ManagePackageVersionsCentrally</c> set to <c>true</c>.
    /// </summary>
    public static bool IsCentralPackageManagementEnabled(string packagesPropsPath)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return false;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        return string.Equals(
            document.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> if a <c>PackageVersion</c> element with
    /// <c>Include="<paramref name="packageId"/>"</c> exists in the given <c>Directory.Packages.props</c> file.
    /// </summary>
    public static bool ContainsCentralPackageVersion(string packagesPropsPath, string packageId)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return false;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        return document.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the <c>Version</c> attribute for a <c>PackageVersion</c> with matching <c>Include</c>, or <see langword="null"/>.
    /// </summary>
    public static string? TryGetCentralPackageVersion(string packagesPropsPath, string packageId)
    {
        if (!File.Exists(packagesPropsPath))
        {
            return null;
        }

        var document = XmlFileLoader.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        var element = document.Descendants("PackageVersion").FirstOrDefault(e =>
            string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        return element?.Attribute("Version")?.Value;
    }

    private static string? FindNearestFile(string? loadedPath, string fileName)
    {
        var directory = ResolveDirectory(loadedPath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static string? ResolveDirectory(string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
        {
            return null;
        }

        if (Directory.Exists(loadedPath))
        {
            return loadedPath;
        }

        if (File.Exists(loadedPath))
        {
            return Path.GetDirectoryName(loadedPath);
        }

        return Path.GetDirectoryName(loadedPath);
    }
}
