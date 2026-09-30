using System.Xml;
using System.Xml.Linq;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Finds MSBuild files that still carry a <c>PackageReference</c> to a package whose central
/// <c>PackageVersion</c> entry is about to be removed from <c>Directory.Packages.props</c>.
/// The scan is rooted at the props file's directory, so it sees projects outside the loaded
/// workspace (for example a test project that is not part of the solution) — the files whose
/// reference would otherwise break with NU1010 once the central version disappears.
/// </summary>
/// <remarks>
/// Text-level by design: the scan ignores <c>Condition</c> attributes, imports and globbing
/// exclusions. That is sufficient for the keep-the-entry / warn decision it feeds. Directories named
/// bin, obj, .git, node_modules and .worktrees (relative to the scan root) are skipped so build
/// output and sibling worktree copies are not counted as consumers.
/// </remarks>
internal static class CentralPackageConsumerScanner
{
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        "node_modules",
        ".worktrees",
    };

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj",
        ".fsproj",
        ".vbproj",
        ".props",
        ".targets",
    };

    /// <summary>
    /// Returns the full paths (sorted) of files under the directory of <paramref name="packagesPropsPath"/>
    /// that contain a <c>PackageReference</c> whose <c>Include</c> equals <paramref name="packageId"/>
    /// (case-insensitive), excluding <paramref name="excludedFilePaths"/>.
    /// </summary>
    public static IReadOnlyList<string> FindConsumers(
        string packagesPropsPath,
        string packageId,
        IEnumerable<string?>? excludedFilePaths = null)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(packagesPropsPath));
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return [];
        }

        var excluded = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var path in excludedFilePaths ?? [])
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                excluded.Add(Path.GetFullPath(path));
            }
        }

        excluded.Add(Path.GetFullPath(packagesPropsPath));

        var consumers = new List<string>();
        foreach (var file in EnumerateCandidateFiles(root))
        {
            if (!excluded.Contains(file) && ReferencesPackage(file, packageId))
            {
                consumers.Add(file);
            }
        }

        consumers.Sort(FileSystemPath.Comparer);
        return consumers;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                if (ScannedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return Path.GetFullPath(file);
                }
            }

            foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
            {
                if (!SkippedDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static bool ReferencesPackage(string filePath, string packageId)
    {
        try
        {
            return XmlFileLoader.Load(filePath).Descendants("PackageReference").Any(element =>
                string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        }
        catch (XmlException)
        {
            // An unparseable file cannot be proven to be a non-consumer. Fall back to a raw text
            // match so the caller errs toward keeping/warning rather than silently dropping a version.
            return File.ReadAllText(filePath).Contains(packageId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
