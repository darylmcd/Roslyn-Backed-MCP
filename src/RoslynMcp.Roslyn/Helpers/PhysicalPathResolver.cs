namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Resolves filesystem paths in physical component order, including symbolic links and junctions.
/// </summary>
public static class PhysicalPathResolver
{
    private const int MaxLinkResolutionDepth = 64;

    /// <summary>
    /// Resolves every existing path component without lexically collapsing a parent traversal
    /// before an earlier filesystem link has been followed.
    /// </summary>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Drive-relative and root-relative paths are ambiguous; use a fully qualified or ordinary relative path.",
                nameof(path));
        }

        return ResolveCore(
            path,
            new HashSet<string>(FileSystemPath.Comparer),
            linkResolutionDepth: 0);
    }

    internal static string GetLinkTargetPath(string linkPath, string rawLinkTarget)
    {
        if (Path.IsPathFullyQualified(rawLinkTarget))
        {
            return rawLinkTarget;
        }

        // Windows drive-relative (`C:target`) and rooted-but-drive-less (`\target`) targets
        // depend on ambient state. Never reinterpret them as link-parent-relative paths.
        if (Path.IsPathRooted(rawLinkTarget))
        {
            throw new IOException(
                $"Filesystem link '{linkPath}' has an ambiguous partially-qualified target.");
        }

        return Path.Join(Path.GetDirectoryName(linkPath), rawLinkTarget);
    }

    private static string ResolveCore(
        string path,
        HashSet<string> activeLinkPaths,
        int linkResolutionDepth)
    {
        if (linkResolutionDepth > MaxLinkResolutionDepth)
        {
            throw new IOException(
                $"Path contains more than {MaxLinkResolutionDepth} nested filesystem links.");
        }

        // Path.GetFullPath would collapse `..` before the operating system resolves an earlier
        // link, changing the physical target of `allowed/link-to-outside/../secret.cs`.
        var absolutePath = Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(Environment.CurrentDirectory, path);
        var pathRoot = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new ArgumentException("Path could not be resolved to a filesystem root.", nameof(path));
        }

        var relativePath = absolutePath[pathRoot.Length..];
        if (string.IsNullOrEmpty(relativePath))
        {
            return Path.GetFullPath(pathRoot);
        }

        var current = pathRoot;
        foreach (var component in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                current = GetPhysicalParent(current);
                continue;
            }

            var next = Path.Combine(current, component);
            var rawLinkTarget = new FileInfo(next).LinkTarget;
            if (rawLinkTarget is null)
            {
                current = next;
                continue;
            }

            var linkKey = Path.GetFullPath(next);
            if (!activeLinkPaths.Add(linkKey))
            {
                throw new IOException($"Filesystem link cycle detected at '{linkKey}'.");
            }

            try
            {
                current = ResolveCore(
                    GetLinkTargetPath(next, rawLinkTarget),
                    activeLinkPaths,
                    linkResolutionDepth + 1);
            }
            finally
            {
                activeLinkPaths.Remove(linkKey);
            }
        }

        return Path.GetFullPath(current);
    }

    private static string GetPhysicalParent(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(parent) ? Path.GetPathRoot(path) ?? path : parent;
    }
}
