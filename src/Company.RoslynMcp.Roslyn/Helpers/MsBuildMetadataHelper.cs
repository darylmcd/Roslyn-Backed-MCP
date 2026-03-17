using System.Xml.Linq;

namespace Company.RoslynMcp.Roslyn.Helpers;

internal static class MsBuildMetadataHelper
{
    public static string? FindDirectoryPackagesProps(string? loadedPath)
    {
        return FindNearestFile(loadedPath, "Directory.Packages.props");
    }

    public static string? FindDirectoryBuildProps(string? loadedPath)
    {
        return FindNearestFile(loadedPath, "Directory.Build.props");
    }

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