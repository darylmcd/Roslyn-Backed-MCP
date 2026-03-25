using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Locates and reads MSBuild metadata files (<c>Directory.Packages.props</c>,
/// <c>Directory.Build.props</c>) by walking the directory hierarchy.
/// </summary>
internal static class MsBuildMetadataHelper
{
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
    /// Searches the directory tree from <paramref name="loadedPath"/> upward for
    /// the nearest <c>Directory.Build.props</c> file.
    /// </summary>
    /// <returns>The absolute path to the file, or <see langword="null"/> if not found.</returns>
    public static string? FindDirectoryBuildProps(string? loadedPath)
    {
        return FindNearestFile(loadedPath, "Directory.Build.props");
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

        var document = XDocument.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
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

        var document = XDocument.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        return document.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
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