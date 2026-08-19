namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Validates caller-supplied folder segments and resolved output paths so a generated
/// document can never land outside its target project's directory. Two failure classes
/// are guarded: (1) hostile segments — rooted paths (<c>C:/evil</c>, <c>\\host\share</c>),
/// traversal (<c>..</c>), separators, and invalid file-name characters — which
/// <see cref="Path.Combine(string[])"/> would otherwise honor by discarding every
/// preceding segment or walking out of the root; and (2) any combined result whose
/// canonical form does not sit strictly under the canonical project root.
///
/// Mirrors the refusal-helper pattern of <see cref="IdentifierValidation"/>: static
/// class, throwing checks, messages that name the offending input. Deliberately does
/// NOT use <c>StartsWith(root)</c> for the containment check — that admits
/// sibling-prefix escapes (<c>C:/Proj</c> vs <c>C:/Proj2</c>); it uses
/// <see cref="Path.GetRelativePath(string, string)"/> and refuses any leading
/// <c>..</c> segment instead.
/// </summary>
internal static class ProjectRelativePathValidation
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when any segment is empty or
    /// whitespace, is <c>.</c> or <c>..</c>, contains a directory separator, is a
    /// rooted path, or contains a character invalid in file names on this platform.
    /// </summary>
    /// <param name="segments">Folder segments that will each become one directory level.</param>
    /// <param name="parameterLabel">Human-readable label for the offending parameter (e.g. "dtoFolders").</param>
    public static void ValidateFolderSegments(IReadOnlyList<string> segments, string parameterLabel)
    {
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                throw new InvalidOperationException(
                    $"{parameterLabel} contains an empty or whitespace path segment " +
                    "(check for doubled, leading, or trailing separators).");

            if (segment is "." or "..")
                throw new InvalidOperationException(
                    $"{parameterLabel} segment '{segment}' is a relative directory reference; " +
                    "folder segments must name directories inside the target project.");

            if (segment.IndexOfAny(Separators) >= 0)
                throw new InvalidOperationException(
                    $"{parameterLabel} segment '{segment}' contains a directory separator; " +
                    "supply one folder name per segment.");

            if (Path.IsPathRooted(segment))
                throw new InvalidOperationException(
                    $"{parameterLabel} segment '{segment}' is a rooted path; " +
                    "folder segments must be relative directory names inside the target project.");

            var invalidIndex = segment.IndexOfAny(Path.GetInvalidFileNameChars());
            if (invalidIndex >= 0)
                throw new InvalidOperationException(
                    $"{parameterLabel} segment '{segment}' contains the invalid file-name " +
                    $"character '{segment[invalidIndex]}'.");
        }
    }

    /// <summary>
    /// Canonicalizes both paths and throws <see cref="InvalidOperationException"/> unless
    /// <paramref name="candidatePath"/> sits strictly under <paramref name="rootDirectory"/>.
    /// Returns the canonical candidate path on success.
    /// </summary>
    /// <param name="rootDirectory">The directory the candidate must live under.</param>
    /// <param name="candidatePath">The path to check.</param>
    /// <param name="parameterLabel">Human-readable label for the offending parameter (e.g. "dtoFolders").</param>
    public static string EnsureDescendantOfRoot(string rootDirectory, string candidatePath, string parameterLabel)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        var firstSegment = relative.Split(Separators, 2)[0];
        if (relative == "." || firstSegment == ".." || Path.IsPathRooted(relative))
            throw new InvalidOperationException(
                $"{parameterLabel} resolves to '{fullCandidate}', which is outside the " +
                $"target project directory '{fullRoot}'.");
        return fullCandidate;
    }
}
