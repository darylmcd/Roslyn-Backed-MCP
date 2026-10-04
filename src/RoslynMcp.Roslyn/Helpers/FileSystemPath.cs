namespace RoslynMcp.Roslyn.Helpers;

internal static class FileSystemPath
{
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Tests lexical strict containment after canonicalizing both paths. Filesystem
    /// links are not resolved; path identity follows the current platform.
    /// </summary>
    public static bool IsStrictDescendant(string rootDirectory, string candidatePath)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 2)[0];
        return relative != "." && firstSegment != ".." && !Path.IsPathRooted(relative);
    }
}
